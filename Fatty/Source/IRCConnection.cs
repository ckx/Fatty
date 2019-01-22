﻿using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Threading;

namespace Fatty
{
    public partial class IRCConnection
    {
        public ServerContext Context { get; set; }

        private TcpClient IrcConnection { get; set; }
        private NetworkStream IrcStream { get; set; }
        private StreamWriter IrcWriter { get; set; }
        private StreamReader IrcReader { get; set; }

        private WelcomeProgress IRCWelcomeProgress;

        private Object WriteLock = new object();

        public event PrivateMessageDelegate PrivateMessageEvent;
        public event NoticeDelegate NoticeEvent;

        public IRCConnection(ServerContext context)
        {
            this.Context = context;
            this.IRCWelcomeProgress = new WelcomeProgress();
        }

        public void ConnectToServer()
        {
            Console.WriteLine("Attempting to connect to: {0}:{1}", Context.ServerURL, Context.ServerPort);

            RegisterEventCallbacks();

            try
            {
                //Establish connection
                this.IrcConnection = new TcpClient(Context.ServerURL, Context.ServerPort);
                this.IrcConnection.ReceiveTimeout = 1000 * 60 * 5;
                this.IrcStream = this.IrcConnection.GetStream();
                this.IrcReader = new StreamReader(this.IrcStream);
                this.IrcWriter = new StreamWriter(this.IrcStream);
                PrintToScreen("Connection Successful");

                // Spawn listener Thread
                Thread th = new Thread(new ThreadStart(ListenForServerMessages));
                th.Start();

                // Send user info
                PrintToScreen("Sending user info...");
                SendServerMessage(String.Format("NICK {0}", Context.Nick));
                SendServerMessage(String.Format("USER {0} 0 * :{1}", Context.Nick, Context.RealName));
            }
            catch (Exception e)
            {
                PrintToScreen("Connection Failed: {0}", e.Message);
            }
        }


        public void SendMessage(string sendTo, string message)
        {
            string outputMessage = String.Format("PRIVMSG {0} :{1}\r\n", sendTo, message);
            SendServerMessage(outputMessage);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(outputMessage);
            Console.ResetColor();
        }

        private void SendServerMessage(string format, params object[] args)
        {
            SendServerMessage(String.Format(format, args));
        }

        private void SendServerMessage(string message)
        {
            lock (WriteLock)
            {
                this.IrcWriter.WriteLine("{0}\r\n", message);
                this.IrcWriter.Flush();
            }
        }

        public void DisconnectOnExit()
        {
            PrintToScreen("Disconnecting Due to Exit");
            SendServerMessage(String.Format("QUIT {0}", Context.QuitMessage));
        }

        private void ListenForServerMessages()
        {
            string ircResponse;
            while ((ircResponse = this.IrcReader.ReadLine()) != null)
            {
                // ignore pings because they just inflate logs
                if (!ircResponse.StartsWith("PING"))
                    PrintToScreen(ircResponse);
                ThreadPool.QueueUserWorkItem(ThreadProc, ircResponse);
            }
        }

        void ThreadProc(object stateInfo)
        {
            // No state object was passed to QueueUserWorkItem, so stateInfo is null.
            DispatchMessageEvents((string)stateInfo);
        }

        private void JoinChannel(string channelName)
        {
            SendServerMessage("JOIN {0}", channelName);
        }

        private void PartChannel(string channelName)
        {
            SendServerMessage("PART {0} {1}", channelName, Context.QuitMessage);
        }

        public void PrintToScreen(string format, params object[] args)
        {
            PrintToScreen(String.Format(format, args));
        }

        public void PrintToScreen(string message)
        {
            if (Context.ShouldPrintToScreen)
            {
                Console.WriteLine(message);
            }
        }

        private void DispatchMessageEvents(string response)
        {
            string[] commandTokens = response.Split(' ');
            if (commandTokens[0][0] == ':')
                commandTokens[0] = commandTokens[0].Remove(0, 1);

            if (commandTokens[0] == "PING")
            {
                HandlePing(commandTokens);
            }

            switch (commandTokens[1])
            {
                // welcome messages
                case "001":
                case "002":
                case "003":
                case "004":
                    {
                        HandleWelcomeMessage(commandTokens);
                        break;
                    }
                case "PRIVMSG":
                    {
                        HandlePrivMsg(commandTokens, response);
                        break;
                    }
                case "NOTICE":
                    {
                        HandleNotice(commandTokens);
                        break;
                    }
                case "INVITE":
                    {
                        HandleInvite(commandTokens);
                        break;
                    }
                case "353":
                    {
                        HandleChannelJoin(commandTokens);
                        break;
                    }
            }
        }

        private void HandleWelcomeMessage(string[] tokens)
        {
            byte welcomeID = Byte.Parse(tokens[1]);
            IRCWelcomeProgress.NotifyOfMessage(welcomeID);
        }

        private void HandlePrivMsg(string[] tokens, string originalMessage)
        {
            string userSender = tokens[0].Substring(0, tokens[0].IndexOf('!'));
            string messageTo = tokens[2];
            string chatMessage = originalMessage.Substring(1 + originalMessage.LastIndexOf(':'));

            if (messageTo[0] == '#' || messageTo[0] == '&')
            {
                Context.HandleServerMessage(userSender, messageTo, chatMessage);
            }
            else
            {
                if (PrivateMessageEvent != null)
                {
                    foreach (PrivateMessageDelegate privDel in PrivateMessageEvent.GetInvocationList())
                    {
                        privDel(userSender, chatMessage);
                    }
                }
            }
        }

        private void HandleNotice(string[] tokens)
        {
            bool bAdminCommand = false;
            if(IsAuthenticatedUser(tokens[0]))
            {
                if (tokens.Length < 5)
                {
                    string userSender = tokens[0].Substring(0, tokens[0].IndexOf('!'));
                    SendMessage(userSender, "Not enough args");
                }
                else
                {
                    string messageCommand = tokens[3].TrimStart(':').ToLower();
                    switch (messageCommand)
                    {
                        case "join":
                            JoinChannel(tokens[4]);
                            bAdminCommand = true;
                            break;
                        case "part":
                        case "leave":
                            PartChannel(tokens[4]);
                            bAdminCommand = true;
                            break;
                        case "msg":
                            // todo: this
                            bAdminCommand = true;
                            break;
                    }
                }
            }

            if (NoticeEvent != null && !bAdminCommand)
            {
                string userSender = tokens[0].Substring(0, tokens[0].IndexOf('!'));
                string noticeMessage = tokens[3].TrimStart(':');
                NoticeEvent(userSender, noticeMessage);
            }
        }

        private void HandleInvite(string[] tokens)
        {
            bool ChannelJoined = false;
            if (IsAuthenticatedUser(tokens[0]))
            {
                JoinChannel(tokens[3].TrimStart(':'));
                ChannelJoined = true;
            }

            if (!ChannelJoined)
            {
                int startIndex = tokens[0][0] == ':' ? 1 : 0;
                int endIndex = tokens[0].IndexOf('!');
                string SendingTo = tokens[0].Substring(startIndex, endIndex - startIndex);
                SendMessage(SendingTo, "Nope, Sorry");
            }
        }

        private void HandleChannelJoin(string[] tokens)
        {
            Context.HandleChannelJoin(tokens[4]);
        }

        private void HandlePing(string[] pingTokens)
        {
            string pingHash = pingTokens[1].Substring(1);
            SendServerMessage("PONG " + pingHash);
        }

        private void OnWelcomeComplete()
        {
            SendMessage("nickserv", "IDENTIFY " + Context.AuthPassword);
            Context.Channels.ForEach((channelContext) => { JoinChannel(channelContext.ChannelName); });
        }

        private void RegisterEventCallbacks()
        {
            IRCWelcomeProgress.WelcomeCompleteEvent += OnWelcomeComplete;
        }

        private bool IsAuthenticatedUser(string UserToken)
        {
            foreach (string authMask in Context.AuthenticatedMasks)
            {
                if (UserToken.Substring(UserToken.IndexOf("@") + 1) == authMask)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

