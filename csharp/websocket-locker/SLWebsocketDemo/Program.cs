using Smartalock.API;
using System.Text.Json.Nodes;

namespace SLWebsocketDemo
{
    class Program
    {

        const string API_HOSTNAME = "1.2.3.4";
        const int API_PORT = 8080;

        const string API_USERNAME = "YOUR_API_USERNAME";
        const string API_PASSWORD = "YOUR_API_PASSWORD";

        static SLWebsocket api;
        static SemaphoreSlim semaphore = new SemaphoreSlim(0, 1);

        static void DisconnectHandler()
        {
            Console.WriteLine("Disconnected delegate called");
            semaphore.Release();
        }

        static void EventHandler(SLEvent e)
        {
            Console.WriteLine("Received event with code=" + e.Code);
            switch (e.Code)
            {
                case (int)SLEventCode.EVENT_CODE_OPEN:
                    Console.WriteLine("Locker opened!");
                    break;
            }
        }

        static void DebugHandler(string message)
        {
            //        Console.WriteLine("DEBUG: {0}", message);
        }


        private static void PrintMenu()
        {
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("Press key to send API command");
            Console.WriteLine("L: Locker listing [GET /locker-list]");
            Console.WriteLine("R: Reservation List [GET /res-list]");
            Console.WriteLine("U: User List [GET /user-list]");
            Console.WriteLine("C: Card List [GET /rfid-list]");
            Console.WriteLine("D: Disconnect");
            Console.WriteLine();
        }

        static async Task Main(string[] args)
        {
            Console.WriteLine("Smartalock Websocket Demo, press ESC to exit...");
            Console.CancelKeyPress += (sender, e) =>
            {

                Console.WriteLine("Exiting...");
                Environment.Exit(0);
            };


            api = new SLWebsocket(API_HOSTNAME, API_PORT, API_USERNAME, API_PASSWORD);
            api.OnDisconnected += DisconnectHandler;
            api.OnEventReceived += EventHandler;
            api.OnDebug += DebugHandler;

            Task taskKeys = new Task(ReadKeys);
            Task taskSmartalock = SmartalockAPI();

            taskKeys.Start();

            var tasks = new[] { taskKeys };
            Task.WaitAll(tasks);
        }


        private static async Task SmartalockAPI()
        {
            do
            {
                Console.WriteLine("Connecting...");
                SLResponse r = await api.Connect();
                if (r.Result)
                {
                    // Connected
                    Console.WriteLine("Connected!");
                    PrintMenu();

                    // Create a semaphore that gets released in the DisconnectHandler
                    semaphore = new SemaphoreSlim(0, 1);
                    // Wait for semaphore to be signalled
                    await semaphore.WaitAsync();
                    semaphore.Release();
                }
                else
                {
                    Console.WriteLine("Connection failed, waiting to reconnect");
                    Thread.Sleep(5000);
                }


            } while (true);
        }

        private static async Task APIArrayListCommand(SLMethod method, string uri, string[] keys)
        {
            SLResponse response = await api.Request(method, uri, null);
            if (response.Result)
            {
                if (response.Info != null)
                {
                    foreach (string key in keys)
                    {
                        Console.Write("{0}\t", key);
                    }
                    Console.WriteLine();

                    JsonArray arr = response.Info.AsArray();
                    foreach (JsonObject obj in arr)
                    {
                        foreach (string key in keys)
                        {
                            //string val = "";
                            if (obj.ContainsKey(key))
                            {
                                Console.Write("{0}\t", obj[key]);
                                //                                val = (string)obj[key];
                            }
                            else
                            {
                                Console.Write("\t");
                            }
                        }
                        Console.WriteLine();
                    }
                }
                else
                {
                    Console.WriteLine("Bad response");
                }
            }
            else
            {
                Console.WriteLine("Failed to get listing");
            }
            PrintMenu();
        }

        private static async Task LockerListCommand()
        {
            await APIArrayListCommand(SLMethod.GET, "/locker-list", new string[] { "key", "uid", "resid", "status", "reserved" });
        }

        private static async Task ReservationListCommand()
        {
            await APIArrayListCommand(SLMethod.GET, "/res-list", new string[] { "resid", "restype", "key" });
        }

        private static async Task UserListCommand()
        {
            await APIArrayListCommand(SLMethod.GET, "/user-list", new string[] { "uid", "name", "email" });
        }

        private static async Task CardListCommand()
        {
            await APIArrayListCommand(SLMethod.GET, "/rfid-list", new string[] { "csn", "uid" });
        }

        private static async Task DisconnectCommand()
        {
            Console.WriteLine("Disconnect requested");
            await api.Disconnect();
            PrintMenu();
        }

        private static void ReadKeys()
        {
            Task t;
            ConsoleKeyInfo key = new ConsoleKeyInfo();

            while (!Console.KeyAvailable && key.Key != ConsoleKey.Escape)
            {

                key = Console.ReadKey(true);

                switch (key.Key)
                {
                    case ConsoleKey.L:
                        t = LockerListCommand();
                        break;

                    case ConsoleKey.R:
                        t = ReservationListCommand();
                        break;

                    case ConsoleKey.U:
                        t = UserListCommand();
                        break;

                    case ConsoleKey.C:
                        t = CardListCommand();
                        break;

                    case ConsoleKey.D:
                        t = DisconnectCommand();
                        break;

                    case ConsoleKey.Escape:
                        break;

                    default:
                        break;
                }
            }
        }
    }

}