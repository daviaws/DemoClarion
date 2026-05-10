using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using ClarionApp;
using ClarionApp.Model;
using ClarionApp.Exceptions;
using Gtk;

namespace ClarionApp
{
	class MainClass
	{
		#region properties
		private WSProxy ws = null;
		private WSProxy wsSpawn = null;
        private ClarionAgent agent;
        String creatureId = String.Empty;
        String creatureName = String.Empty;

		private static readonly Random random = new Random();
		private const int WORLD_X_MIN = 60;
		private const int WORLD_X_MAX = 737;
		private const int WORLD_Y_MIN = 57;
		private const int WORLD_Y_MAX = 552;
		// growingRate: 0 = no continuous spawn, 1-10 = items per 5s cycle
		private int growingRate = 0;
		#endregion

		#region constructor
		public MainClass() {
			Application.Init();
			Console.WriteLine ("ClarionApp V0.8");

			String envRate = Environment.GetEnvironmentVariable("GROWING_RATE");
			int parsed;
            if (Int32.TryParse(envRate, out parsed))
				growingRate = Math.Max(0, Math.Min(10, parsed));
			Console.WriteLine(String.Format("Growing Rate: {0}/10", growingRate));

			try
            {
				String message = String.Empty;
                // Retry logic 10 times cooldown of 3 seconds
				for (int attempt = 1; attempt <= 10; attempt++) {
					Console.Out.WriteLine(String.Format("[Connect] Attempt {0}/10", attempt));
					try {
						ws = new WSProxy("localhost", 4011);
						message = ws.Connect();
						if (ws.IsConnected) break;
					} catch (Exception) {}
					Console.Out.WriteLine("[Connect] Retrying in 3s...");
					Thread.Sleep(3000);
				}

                if (ws != null && ws.IsConnected)
                {
                    Console.Out.WriteLine ("[SUCCESS] " + message + "\n");
					ws.SendWorldReset();
                    ws.NewCreature(400, 200, 0, out creatureId, out creatureName);
					ws.SendCreateLeaflet();
                    ws.NewBrick(4, 747, 2, 800, 567);
                    ws.NewBrick(4, 50, -4, 747, 47);
                    ws.NewBrick(4, 49, 562, 796, 599);
                    ws.NewBrick(4, -2, 6, 50, 599);

					// Second dedicated connection for spawning
					wsSpawn = new WSProxy("localhost", 4011);
					wsSpawn.Connect();
					SpawnInitialItems();

                    if (!String.IsNullOrWhiteSpace(creatureId))
                    {
                        ws.SendStartCamera(creatureId);
                        ws.SendStartCreature(creatureId);
                    }

                    Console.Out.WriteLine("Creature created with name: " + creatureId + "\n");
					agent = new ClarionAgent(ws,creatureId,creatureName);
                    agent.Run();
					Console.Out.WriteLine("Running Simulation ...\n");

					if (growingRate > 0) {
                        String spotResponse = ws.NewDeliverySpot(4, 300, 300);
                        Console.WriteLine("[MainClass] NewDeliverySpot response: '" + spotResponse + "'");
						Thread spawnThread = new Thread(SpawnLoop);
						spawnThread.IsBackground = true;
						spawnThread.Start();
					}
                }
				else {
					Console.Out.WriteLine("The WorldServer3D engine was not found ! You must start WorldServer3D before running this application !");
					System.Environment.Exit(1);
				}
            }
            catch (WorldServerInvalidArgument invalidArtgument)
            {
                Console.Out.WriteLine(String.Format("[ERROR] Invalid Argument: {0}\n", invalidArtgument.Message));
            }
            catch (WorldServerConnectionError serverError)
            {
                Console.Out.WriteLine(String.Format("[ERROR] Is is not possible to connect to server: {0}\n", serverError.Message));
            }
            catch (Exception ex)
            {
                Console.Out.WriteLine(String.Format("[ERROR] Unknown Error: {0}\n", ex.Message));
            }
			Application.Run();
		}
		#endregion

		#region Methods
		public static void Main (string[] args)	{
			new MainClass();
		}

		private void SpawnInitialItems() {
			int count = Math.Max(0, growingRate * 2);
			Console.Out.WriteLine(String.Format("[Spawn] Initial: {0} jewels + {0} foods", count));
			for (int i = 0; i < count; i++) {
				wsSpawn.NewJewel(random.Next(0, 6), random.Next(WORLD_X_MIN, WORLD_X_MAX), random.Next(WORLD_Y_MIN, WORLD_Y_MAX));
				wsSpawn.NewFood(random.Next(0, 3),  random.Next(WORLD_X_MIN, WORLD_X_MAX), random.Next(WORLD_Y_MIN, WORLD_Y_MAX));
			}
		}

        private void SpawnLoop() {
            while (true) {
                Thread.Sleep(5000);
                for (int i = 0; i < growingRate; i++) {
                    try {
                        if (random.NextDouble() < 0.6)
                            wsSpawn.NewJewel(random.Next(0, 6), random.Next(WORLD_X_MIN, WORLD_X_MAX), random.Next(WORLD_Y_MIN, WORLD_Y_MAX));
                        else
                            wsSpawn.NewFood(random.Next(0, 3), random.Next(WORLD_X_MIN, WORLD_X_MAX), random.Next(WORLD_Y_MIN, WORLD_Y_MAX));
                    } catch (Exception e) {
                        Console.WriteLine("[Spawn] Error: " + e.Message);
                    }
                    Thread.Sleep(200); // spread spawns to reduce concurrent modification window
                }
                Console.Out.WriteLine(String.Format("[Spawn] +{0} items", growingRate));
            }
        }
        #endregion
	}
}