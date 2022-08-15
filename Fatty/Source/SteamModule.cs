﻿using RestSharp;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Fatty
{
    class SteamModule : FattyModule
    {

        [DataContract]
        public class SteamConfig 
        {
            [DataMember(IsRequired = true)]
            public string APIKey;

            [DataMember(IsRequired = true)]
            public int PollFrequencyMinutes;

            [DataMember]
            public List<SteamChannelContext> Contexts;
        }

        [DataContract]
        public class SteamChannelContext
        {
            [DataMember(IsRequired = true)]
            public string ServerName;

            [DataMember(IsRequired = true)]
            public string ChannelName;

            [DataMember(IsRequired = true)]
            public int MinPlayerThreshold;

            [DataMember(IsRequired = true)]
            public int MessageCooldownMinutes;

            [IgnoreDataMember]
            public DateTime LastMessage;
        }

        [DataContract]
        public class SteamServerResult
        {
            [DataMember(Name = "response")]
            public SteamServerResponse Response;
        }

        [DataContract]
        public class SteamServerResponse
        {
            [DataMember(Name = "servers")]
            public List<SteamGameServer> Servers;
        }

        [DataContract]
        public class SteamGameServer
        {
            [DataMember(Name = "addr")]
            public string IPAddress;

            [DataMember(Name = "steamid")]
            public string SteamID;

            [DataMember(Name = "name")]
            public string ServerName;

            [DataMember(Name = "appid")]
            public long AppID;

            [DataMember(Name = "product")]
            public string ProductName;

            [DataMember(Name = "players")]
            public int PlayerCount;
            
        }

        SteamConfig Config;

        CancellationTokenSource PollCancellationSource;

        public override void ChannelInit(ChannelContext channel)
        {
            base.ChannelInit(channel);
        }

        public override void PostConnectionModuleInit()
        {
            base.PostConnectionModuleInit();

            // todo: make it so I only care about my own contexts
            Config = FattyHelpers.DeserializeFromPath<SteamConfig>("Steam.cfg");
        }

        public override void RegisterEvents()
        {
            
        }

        public override void ListCommands(ref List<string> CommandNames)
        {
            
        }

        public override void RegisterAvailableCommands(ref List<UserCommand> Commands)
        {
            OwningChannel.ChannelJoinedEvent += OnChannelJoined;
        }

        void OnChannelJoined(string ircChannel)
        {
            // todo: move this somewhere else
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            PollNeotokyoServers(tokenSource.Token);
        }

        async void PollNeotokyoServers(CancellationToken cancel)
        {
            RestClient client = new RestClient("https://api.steampowered.com");

            RestRequest request = new RestRequest("IGameServersService/GetServerList/v1/");
            request.AddQueryParameter("key", Config.APIKey);
            request.AddQueryParameter("filter", @"gamedir\NeotokyoSource\empty\1");

            TimeSpan PollInterval = TimeSpan.FromMinutes(Config.PollFrequencyMinutes);
            TimeSpan SleepTime = TimeSpan.FromMinutes(Config.Contexts[0].MessageCooldownMinutes);

            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    var result = await client.ExecuteAsync(request);
                    if (result.IsSuccessful)
                    {
                        try
                        {
                            SteamServerResult pollResult = FattyHelpers.DeserializeFromJsonString<SteamServerResult>(result.Content);
                            SteamServerResponse pollResponse = pollResult.Response;
                            if (pollResponse != null)
                            {
                                if (pollResponse.Servers != null && pollResponse.Servers.Count > 0)
                                {
                                    var firstServer = pollResponse.Servers[0];
                                    OwningChannel.SendChannelMessage($"AHHHHHHHHHHHHHHHH, netokyo server \"{pollResponse.Servers[0].ServerName}\" has more than {Config.Contexts[0].MinPlayerThreshold} nerds in it");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Fatty.PrintWarningToScreen(ex.Message, ex.StackTrace);
                        }

                        await Task.Delay(SleepTime);
                    }
                    else
                    {
                        await Task.Delay(PollInterval);
                    }
                }
                catch(Exception ex)
                {
                    Fatty.PrintWarningToScreen(ex.Message, ex.StackTrace);
                }
            }
            
        }
    }
}
