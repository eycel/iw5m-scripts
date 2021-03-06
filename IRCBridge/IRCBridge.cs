﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using InfinityScript;
using Meebey.SmartIrc4net;

namespace IRCBridge
{
    public class IRCBridge : BaseScript
    {
        public IrcClient irc = new IrcClient();
        public string server;
        public int port;
        public string channel;
        public string nick;
        public string password;
        public string serverpassword;
        public bool sendstartmsg;
        public bool destroy;
        public bool sendingscores;
        public Thread thread;
        public const string BOLD = "";
        public const string NORMAL = "";
        public const string UNDERLINE = "";
        public const string COLOUR = "";

        public IRCBridge()
        {
            /*AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                                                              {
                                                                  Log.Error("Unhandled exception:");
                                                                  Log.Error(args.ExceptionObject.ToString());
                                                                  Environment.Exit(1);
                                                              };*/
            if (File.Exists("irc.lock"))
            {
                Log.Error("IRC lock file found, plugin was not properly disposed of.");
                //return;
            }
            File.WriteAllText("irc.lock", "");
            try
            {
                var settings = File.ReadAllLines("scripts\\ircbridge\\settings.txt");
                if (settings.Length < 6)
                    throw new Exception();
                server = settings[0];
                port = int.Parse(settings[1]);
                channel = settings[2];
                nick = settings[3];
                password = settings[4];
                serverpassword = settings[5];
            }
            catch (Exception e)
            {
                Log.Error("settings.txt not found or is invalid.");
                return;
            }
            irc.Encoding = Encoding.ASCII;
            irc.SendDelay = 300;
            //irc.AutoReconnect = true;
            irc.AutoRejoinOnKick = true;
            irc.SupportNonRfc = true;
            irc.ActiveChannelSyncing = true;
            irc.OnChannelMessage += IRCOnOnChannelMessage;
#if DEBUG
            irc.OnRawMessage += (sender, args) => Log.Debug(args.Data.RawMessage);
#endif
            Tick += () =>
                        {
                            /*if (irc.IsConnected)
                                irc.ListenOnce(false);
                            /*if (!init)
                            {
                                Connect();
                                init = true;
                            }*/
                            if (sendstartmsg)
                            {
                                sendstartmsg = false;
                                OnStartGameType();
                            }
                        };
            PlayerConnected +=
                entity => SendMessage(entity.GetField<string>("name") + " has connected to the game.");
            PlayerConnecting +=
                entity => SendMessage(entity.GetField<string>("name") + " is connecting to the game.");
            PlayerDisconnected +=
                entity => SendMessage(entity.GetField<string>("name") + " has disconnected from the game.");
            OnNotify("exitLevel_called", OnExitLevel);
            OnNotify("game_ended", OnExitLevel2);
            Log.Info("Connecting to " + server + ":" + port + "/" + channel);
            thread = new Thread(Connect);
            thread.Start();
        }

        ~IRCBridge()
        {
            Destroy();
        }

        private void Destroy()
        {
            if (destroy)
                return;
            Thread.Sleep(1000);
            var start = DateTime.Now;
            while (sendingscores && (DateTime.Now - start).TotalSeconds < 3)
                Thread.Sleep(10);
            destroy = true;
            if (irc.IsConnected)
            {
                Debug.WriteLine("Disconnecting from IRC.");
                irc.Disconnect();
                Debug.WriteLine("Disconnected");
            }
            //Log.Debug("Plugin getting destroyed...");
            Debug.WriteLine("Plugin getting destroyed...");
            //SendMessage("Plugin getting destroyed...");
            Debug.WriteLine("Nulling IRC client...");
            //Log.Debug("Nulling IRC client...");
            irc = null;
            Debug.WriteLine("Deleting lock file...");
            //Log.Debug("Deleting irc.lock file");
            File.Delete("irc.lock");
            Debug.WriteLine("Calling GC collect");
            GC.Collect();
            Debug.WriteLine("Destroy done.");
           // Log.Debug("Destroy done.");
        }

        private void IRCOnOnChannelMessage(object sender, IrcEventArgs ircEventArgs)
        {
            //TODO: Find out how to say stuff without resorting to renaming players
            switch (ircEventArgs.Data.Message)
            {
                case "!players":
                    BuildScores();
                    break;
                default:
                    foreach (var player in Players)
                    {
                        var name = player.GetField<string>("name");
                        player.SetField("name", "^8[IRC] " + ircEventArgs.Data.Nick);
                        player.Call("sayall", ircEventArgs.Data.Message);
                        player.SetField("name", name);
                        break;
                    }
                    break;
            }
        }

        private void BuildScores()
        {
            SendMessage("Player          Score    Kills Assists Deaths");
            var scoreList = (from p in Players
                             orderby p.GetField<int>("score") descending, p.GetField<int>("deaths") ascending
                             select p).ToArray();
            for (int i = 0; i < scoreList.Length; i++)
            {
                SendMessage(string.Format("{0} {1} {2} {3} {4}", scoreList[i].GetField<string>("name").PadRight(15),
                                          scoreList[i].GetField<string>("score").PadRight(8),
                                          scoreList[i].GetField<string>("kills").PadRight(5),
                                          scoreList[i].GetField<string>("assists").PadRight(7),
                                          scoreList[i].GetField<string>("deaths").PadRight(6)));
            }
        }

        public override void OnExitLevel()
        {
            //Log.Debug("Sending match stats...");
            sendingscores = true;
            SendMessage("Match ended, level is exiting...");
            SendMessage("Scoreboard: ");
            //Log.Debug("Constructing match stats...");
            BuildScores();
            sendingscores = false;
            Destroy();
        }

        public void OnExitLevel2(Parameter para)
        {
            sendingscores = true;
            Log.Debug("Sending match stats...");
            var winner = "";
            switch (para.Type)
            {
                case VariableType.String:
                    winner = para.As<string>();
                    break;
                case VariableType.Entity:
                    if (para.As<Entity>().IsPlayer)
                        winner = para.As<Entity>().GetField<string>("name");
                    break;
            }
            SendMessage("Match ended, level is exiting...");
            SendMessage("Scoreboard (Winner is " + winner + "): ");
            //Log.Debug("Constructing match stats...");
            BuildScores();
            irc.ListenOnce(false);
            Thread.Sleep(500);
            sendingscores = false;
            Destroy();
        }

        public override void OnStartGameType()
        {
            SendMessage("Match starting. Map: " + Call<string>("GetDvar", new Parameter("mapname")) + ", Game Type: " +
                        Call<string>("GetDvar", new Parameter("g_gametype")));
        }

        public override void OnSay(Entity player, string name, string message)
        {
            var teamcolour = NORMAL;
            switch (player.GetField<string>("sessionteam"))
            {
                case "axis":
                    teamcolour = "04";
                    break;
                case "allies":
                    teamcolour = "02";
                    break;
            }
            SendMessage(COLOUR + teamcolour + name + NORMAL + ": " + message);
        }

        public override void OnPlayerKilled(Entity player, Entity inflictor, Entity attacker, int damage, string mod, string weapon, Vector3 dir, string hitLoc)
        {
            if (mod == "MOD_SUICIDE" || mod == "MOD_TRIGGER_HURT" || mod == "MOD_FALLING")
            {
                var colour = NORMAL;
                switch (player.GetField<string>("sessionteam"))
                {
                    case "axis":
                        colour = "04";
                        break;
                    case "allies":
                        colour = "02";
                        break;
                }
                SendMessage(string.Format("{1}{0}{2} suicided.", player.GetField<string>("name"),
                                COLOUR + colour, NORMAL));
            }
            else
            {
                var playercolour = NORMAL;
                switch (player.GetField<string>("sessionteam"))
                {
                    case "axis":
                        playercolour = "04";
                        break;
                    case "allies":
                        playercolour = "02";
                        break;
                }
                var attackercolour = NORMAL;
                switch (attacker.GetField<string>("sessionteam"))
                {
                    case "axis":
                        attackercolour = "04";
                        break;
                    case "allies":
                        attackercolour = "02";
                        break;
                }
                SendMessage(string.Format("{3}{0}{5} was killed by {4}{1}{5} with {2}.", player.GetField<string>("name"),
                                          attacker.GetField<string>("name"), weapon, COLOUR + playercolour,
                                          COLOUR + attackercolour, NORMAL));
            }
        }


        public void SendMessage(string message)
        {
            if (irc != null)
                if (irc.IsConnected)
                {
                    irc.SendMessage(SendType.Message, channel, ReplaceQuakeColorCodes(message));
                    irc.ListenOnce(false);
                    irc.ListenOnce(false);
                }
        }

        public void Connect()
        {
            try
            {
                Log.Info("Attempting to connect...");
                irc.Connect(server, port);
                irc.ListenOnce(false);
                Log.Info("Logging in.");
                irc.Login(nick, "IW5M IRCBridge Bot", 0, nick, serverpassword);
                irc.ListenOnce(false);
                Log.Info("Joining channel.");
                irc.RfcJoin(channel);
                irc.ListenOnce(false);
                Log.Info("Identifying.");
                irc.RfcPrivmsg("NickServ", "identify " + password);
                irc.ListenOnce(false);
                //Log.Info("Sending match info.");
                //OnStartGameType();
                sendstartmsg = true;
                Log.Info("Listening.");
                irc.Listen();
                //irc.Disconnect();
            }
            catch (ThreadAbortException e)
            {
                Log.Debug("ABORT ABORT ABORT");
                if (irc.IsConnected)
                    irc.Disconnect();
                Log.Debug("Destroying...");
                Destroy();
                return;
            }
            catch (Exception e)
            {
                //throw;
                Log.Error(e.ToString());
                Destroy();
                return;
            }
        }

        public static string ReplaceQuakeColorCodes(string remove)
        {
            var filteredout = "";
            var array = remove.Split('^');
            foreach (var part in array)
            {
                if (part.StartsWith("0"))
                    filteredout += part.Substring(1) + COLOUR + "01";
                else if (part.StartsWith("1"))
                    filteredout += part.Substring(1) + COLOUR + "04";
                else if (part.StartsWith("2"))
                    filteredout += part.Substring(1) + COLOUR + "03";
                else if (part.StartsWith("3"))
                    filteredout += part.Substring(1) + COLOUR + "08";
                else if (part.StartsWith("4"))
                    filteredout += part.Substring(1) + COLOUR + "02";
                else if (part.StartsWith("5"))
                    filteredout += part.Substring(1) + COLOUR + "12";
                else if (part.StartsWith("6"))
                    filteredout += part.Substring(1) + COLOUR + "06";
                else if (part.StartsWith("7"))
                    //filteredout += part.Substring(1) + COLOUR + "00";
                    filteredout += part.Substring(1) + NORMAL;
                else if (part.StartsWith("8"))
                    filteredout += part.Substring(1) + COLOUR + "14";
                else if (part.StartsWith("9"))
                    filteredout += part.Substring(1) + COLOUR + "05";
                else
                    filteredout += "^" + part;
            }
            return filteredout.Substring(1);
        }
    }
}
