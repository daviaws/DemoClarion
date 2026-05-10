using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using Clarion;
using Clarion.Framework;
using Clarion.Framework.Core;
using Clarion.Framework.Templates;
using ClarionApp.Model;
using ClarionApp;
using System.Threading;
using Gtk;

namespace ClarionApp
{
    public enum CreatureActions
    {
        DO_NOTHING,
        ROTATE_CLOCKWISE,
        GO_AHEAD,
        MOVE_TO_JEWEL,
        PICK_UP_JEWEL,
        MOVE_TO_DEPOT,
        DELIVER_JEWELS,
        MOVE_TO_FOOD,
        EAT_FOOD,
        STOP
    }

    public class ClarionAgent
    {
        #region Constants

        private const string SENSOR_VISUAL  = "VisualSensor";
        private const string SENSOR_GOAL    = "GoalSensor";
        private const string SENSOR_LEAFLET = "LeafletSensor";
        private const string SENSOR_FUEL    = "FuelSensor";

        private const string DIM_WALL_AHEAD       = "WallAhead";
        private const string DIM_JEWEL_VISIBLE    = "JewelVisible";
        private const string DIM_JEWEL_CLOSE      = "JewelClose";
        private const string DIM_AT_DEPOT         = "AtDepot";
        private const string DIM_LEAFLET_COMPLETE = "LeafletComplete";
        private const string DIM_GOAL_COLLECT     = "GoalCollect";
        private const string DIM_GOAL_DELIVER     = "GoalDeliver";
        private const string DIM_GOAL_DONE        = "GoalDone";
        private const string DIM_ENERGY_LOW       = "EnergyLow";
        private const string DIM_FOOD_VISIBLE     = "FoodVisible";
        private const string DIM_FOOD_CLOSE       = "FoodClose";
        private const string DIM_DEPOT_FOUND      = "DepotFound";

        private const double DISTANCE_PICK_UP    = 40.0;
        private const double DISTANCE_DELIVER    = 80.0;
        private const double DISTANCE_WALL       = 61.0;
        private const double DISTANCE_EAT        = 40.0;
        private const double NAV_SPEED           = 2.0;
        private const double FUEL_LOW_THRESHOLD  = 300.0;

        private const int GOAL_COLLECT = 0;
        private const int GOAL_DELIVER = 1;
        private const int GOAL_DONE    = 2;

        private const string WEIGHTS_SAVE_PATH = "/tmp/clarion_data/bl_weights.dat";
        private const int    SAVE_EVERY_CYCLES = 100;

        #endregion

        #region Properties

        public MindViewer mind;
        private string creatureId   = string.Empty;
        private string creatureName = string.Empty;

        public double MaxNumberOfCognitiveCycles = -1;
        private double currentCognitiveCycle     = 0;
        public int TimeBetweenCognitiveCycles    = 100;

        private Thread  runThread;
        private WSProxy worldServer;
        private Clarion.Framework.Agent currentAgent;
        private SimplifiedQBPNetwork blNet;

        private int activeGoal       = GOAL_COLLECT;
        private int goalBeforeEating = -1;

        // Perceptual inputs
        private DimensionValuePair inputWallAhead;
        private DimensionValuePair inputJewelVisible;
        private DimensionValuePair inputJewelClose;
        private DimensionValuePair inputAtDepot;
        private DimensionValuePair inputLeafletComplete;
        private DimensionValuePair inputGoalCollect;
        private DimensionValuePair inputGoalDeliver;
        private DimensionValuePair inputGoalDone;
        private DimensionValuePair inputEnergyLow;
        private DimensionValuePair inputFoodVisible;
        private DimensionValuePair inputFoodClose;
        private DimensionValuePair inputDepotFound;

        // Action outputs
        private ExternalActionChunk outputRotateClockwise;
        private ExternalActionChunk outputGoAhead;
        private ExternalActionChunk outputMoveToJewel;
        private ExternalActionChunk outputPickUpJewel;
        private ExternalActionChunk outputMoveToDepot;
        private ExternalActionChunk outputDeliverJewels;
        private ExternalActionChunk outputMoveToFood;
        private ExternalActionChunk outputEatFood;
        private ExternalActionChunk outputStop;

        // Runtime navigation state
        private double creatureX     = 0;
        private double creatureY     = 0;
        private double creaturePitch = 0;
        private double creatureFuel  = 100;
        private Thing  targetJewel   = null;
        private Thing  targetFood    = null;
        private double depotX        = 0;
        private double depotY        = 0;
        private bool   depotFound    = false;

        #endregion

        #region Constructor

        public ClarionAgent(WSProxy nws, string creature_ID, string creature_Name)
        {
            worldServer  = nws;
            creatureId   = creature_ID;
            creatureName = creature_Name;

            currentAgent = World.NewAgent("Creature");

            mind = new MindViewer();
            mind.Show();

            inputWallAhead       = World.NewDimensionValuePair(SENSOR_VISUAL,  DIM_WALL_AHEAD);
            inputJewelVisible    = World.NewDimensionValuePair(SENSOR_VISUAL,  DIM_JEWEL_VISIBLE);
            inputJewelClose      = World.NewDimensionValuePair(SENSOR_VISUAL,  DIM_JEWEL_CLOSE);
            inputAtDepot         = World.NewDimensionValuePair(SENSOR_VISUAL,  DIM_AT_DEPOT);
            inputLeafletComplete = World.NewDimensionValuePair(SENSOR_LEAFLET, DIM_LEAFLET_COMPLETE);
            inputGoalCollect     = World.NewDimensionValuePair(SENSOR_GOAL,    DIM_GOAL_COLLECT);
            inputGoalDeliver     = World.NewDimensionValuePair(SENSOR_GOAL,    DIM_GOAL_DELIVER);
            inputGoalDone        = World.NewDimensionValuePair(SENSOR_GOAL,    DIM_GOAL_DONE);
            inputEnergyLow       = World.NewDimensionValuePair(SENSOR_FUEL,    DIM_ENERGY_LOW);
            inputFoodVisible     = World.NewDimensionValuePair(SENSOR_VISUAL,  DIM_FOOD_VISIBLE);
            inputFoodClose       = World.NewDimensionValuePair(SENSOR_VISUAL,  DIM_FOOD_CLOSE);
            inputDepotFound      = World.NewDimensionValuePair(SENSOR_VISUAL,  DIM_DEPOT_FOUND);

            outputRotateClockwise = World.NewExternalActionChunk(CreatureActions.ROTATE_CLOCKWISE.ToString());
            outputGoAhead         = World.NewExternalActionChunk(CreatureActions.GO_AHEAD.ToString());
            outputMoveToJewel     = World.NewExternalActionChunk(CreatureActions.MOVE_TO_JEWEL.ToString());
            outputPickUpJewel     = World.NewExternalActionChunk(CreatureActions.PICK_UP_JEWEL.ToString());
            outputMoveToDepot     = World.NewExternalActionChunk(CreatureActions.MOVE_TO_DEPOT.ToString());
            outputDeliverJewels   = World.NewExternalActionChunk(CreatureActions.DELIVER_JEWELS.ToString());
            outputMoveToFood      = World.NewExternalActionChunk(CreatureActions.MOVE_TO_FOOD.ToString());
            outputEatFood         = World.NewExternalActionChunk(CreatureActions.EAT_FOOD.ToString());
            outputStop            = World.NewExternalActionChunk(CreatureActions.STOP.ToString());

            runThread = new Thread(CognitiveCycle);

            Console.WriteLine("[ClarionAgent] Agent initialized.");
        }

        #endregion

        #region Public Methods

        public void Run()
        {
            Console.WriteLine("[ClarionAgent] Starting ...");
            if (runThread != null && !runThread.IsAlive)
            {
                SetupAgentInfrastructure();
                runThread.Start(null);
            }
        }

        public void Abort(bool deleteAgent)
        {
            Console.WriteLine("[ClarionAgent] Aborting ...");
            if (runThread != null && runThread.IsAlive)
                runThread.Abort();
            SaveWeights();
            if (currentAgent != null && deleteAgent)
                currentAgent.Die();
        }

        #endregion

        #region Persistence

        private void SaveWeights()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(WEIGHTS_SAVE_PATH));
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var type  = blNet.GetType().BaseType;

                var ih = new Dictionary<Guid, List<double>>(
                    (Dictionary<Guid, List<double>>)type.GetProperty("InputToHiddenWeights", flags).GetValue(blNet));
                var ho = new List<Dictionary<Guid, double>>(
                    (List<Dictionary<Guid, double>>)type.GetProperty("HiddenToOutputWeights", flags).GetValue(blNet));
                var ht = new List<double>(
                    (List<double>)type.GetProperty("HiddenThresholds", flags).GetValue(blNet));

                using (BinaryWriter bw = new BinaryWriter(File.Open(WEIGHTS_SAVE_PATH, FileMode.Create)))
                {
                    bw.Write(ih.Count);
                    foreach (var kv in ih) {
                        bw.Write(kv.Key.ToByteArray(), 0, 16);
                        var listCopy = new List<double>(kv.Value);
                        bw.Write(listCopy.Count);
                        foreach (double d in listCopy) bw.Write(d);
                    }
                    bw.Write(ho.Count);
                    foreach (var dict in ho) {
                        var dictCopy = new Dictionary<Guid, double>(dict);
                        bw.Write(dictCopy.Count);
                        foreach (var kv in dictCopy) {
                            bw.Write(kv.Key.ToByteArray(), 0, 16);
                            bw.Write(kv.Value);
                        }
                    }
                    bw.Write(ht.Count);
                    foreach (double d in ht) bw.Write(d);
                }
                Console.WriteLine("[ClarionAgent] Weights saved.");
            }
            catch (Exception e) { Console.WriteLine("[ClarionAgent] Save failed: " + e.Message); }
        }

        private void LoadWeights()
        {
            if (!File.Exists(WEIGHTS_SAVE_PATH)) {
                Console.WriteLine("[ClarionAgent] No saved weights — starting fresh.");
                return;
            }
            try
            {
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var type  = blNet.GetType().BaseType;

                var ih = (Dictionary<Guid, List<double>>)type.GetProperty("InputToHiddenWeights", flags).GetValue(blNet);
                var ho = (List<Dictionary<Guid, double>>)type.GetProperty("HiddenToOutputWeights", flags).GetValue(blNet);
                var ht = (List<double>)                  type.GetProperty("HiddenThresholds", flags).GetValue(blNet);

                using (BinaryReader br = new BinaryReader(File.Open(WEIGHTS_SAVE_PATH, FileMode.Open)))
                {
                    int ihCount = br.ReadInt32();
                    for (int i = 0; i < ihCount; i++) {
                        Guid g = new Guid(br.ReadBytes(16));
                        int len = br.ReadInt32();
                        var list = new List<double>(len);
                        for (int j = 0; j < len; j++) list.Add(br.ReadDouble());
                        if (ih.ContainsKey(g)) ih[g] = list; else ih.Add(g, list);
                    }
                    int hoCount = br.ReadInt32();
                    for (int i = 0; i < hoCount && i < ho.Count; i++) {
                        int dCount = br.ReadInt32();
                        for (int j = 0; j < dCount; j++) {
                            Guid g = new Guid(br.ReadBytes(16));
                            double v = br.ReadDouble();
                            if (ho[i].ContainsKey(g)) ho[i][g] = v; else ho[i].Add(g, v);
                        }
                    }
                    int htCount = br.ReadInt32();
                    ht.Clear();
                    for (int i = 0; i < htCount; i++) ht.Add(br.ReadDouble());
                }
                Console.WriteLine("[ClarionAgent] Weights loaded.");
            }
            catch (Exception e) { Console.WriteLine("[ClarionAgent] Load failed, starting fresh: " + e.Message); }
        }

        #endregion

        #region Infrastructure Setup

        private void SetupAgentInfrastructure()
        {
            SetupACS();
            LoadWeights();
        }

        private void SetupACS()
        {
            blNet = AgentInitializer.InitializeImplicitDecisionNetwork(
                currentAgent, SimplifiedQBPNetwork.Factory);

            blNet.Input.Add(inputWallAhead);
            blNet.Input.Add(inputJewelVisible);
            blNet.Input.Add(inputJewelClose);
            blNet.Input.Add(inputAtDepot);
            blNet.Input.Add(inputLeafletComplete);
            blNet.Input.Add(inputGoalCollect);
            blNet.Input.Add(inputGoalDeliver);
            blNet.Input.Add(inputGoalDone);
            blNet.Input.Add(inputEnergyLow);
            blNet.Input.Add(inputFoodVisible);
            blNet.Input.Add(inputFoodClose);
            blNet.Input.Add(inputDepotFound);

            blNet.Output.Add(outputRotateClockwise);
            blNet.Output.Add(outputGoAhead);
            blNet.Output.Add(outputMoveToJewel);
            blNet.Output.Add(outputMoveToDepot);
            blNet.Output.Add(outputMoveToFood);
            blNet.Output.Add(outputEatFood);

            blNet.Parameters.LEARNING_RATE = 0.5;
            blNet.Parameters.MOMENTUM      = 0.1;

            currentAgent.Commit(blNet);

            SupportCalculator sc;
            FixedRule r;

            sc = FixedRuleEat;
            r  = AgentInitializer.InitializeActionRule(currentAgent, FixedRule.Factory, outputEatFood, sc);
            currentAgent.Commit(r);

            sc = FixedRuleMoveToFood;
            r  = AgentInitializer.InitializeActionRule(currentAgent, FixedRule.Factory, outputMoveToFood, sc);
            currentAgent.Commit(r);

            sc = FixedRuleAvoidWall;
            r  = AgentInitializer.InitializeActionRule(currentAgent, FixedRule.Factory, outputRotateClockwise, sc);
            currentAgent.Commit(r);

            sc = FixedRulePickUp;
            r  = AgentInitializer.InitializeActionRule(currentAgent, FixedRule.Factory, outputPickUpJewel, sc);
            currentAgent.Commit(r);

            sc = FixedRuleMoveToJewel;
            r  = AgentInitializer.InitializeActionRule(currentAgent, FixedRule.Factory, outputMoveToJewel, sc);
            currentAgent.Commit(r);

            sc = FixedRuleGoToDepotAfterCollect;
            r  = AgentInitializer.InitializeActionRule(currentAgent, FixedRule.Factory, outputMoveToDepot, sc);
            currentAgent.Commit(r);

            sc = FixedRuleMoveToDepot;
            r  = AgentInitializer.InitializeActionRule(currentAgent, FixedRule.Factory, outputMoveToDepot, sc);
            currentAgent.Commit(r);

            sc = FixedRuleDeliver;
            r  = AgentInitializer.InitializeActionRule(currentAgent, FixedRule.Factory, outputDeliverJewels, sc);
            currentAgent.Commit(r);

            sc = FixedRuleStop;
            r  = AgentInitializer.InitializeActionRule(currentAgent, FixedRule.Factory, outputStop, sc);
            currentAgent.Commit(r);

            currentAgent.ACS.Parameters.PERFORM_RER_REFINEMENT        = true;
            currentAgent.ACS.Parameters.LEVEL_SELECTION_METHOD        =
                ActionCenteredSubsystem.LevelSelectionMethods.STOCHASTIC;
            currentAgent.ACS.Parameters.LEVEL_SELECTION_OPTION        =
                ActionCenteredSubsystem.LevelSelectionOptions.FIXED;
            currentAgent.ACS.Parameters.FIXED_FR_LEVEL_SELECTION_MEASURE  = 0.7;
            currentAgent.ACS.Parameters.FIXED_BL_LEVEL_SELECTION_MEASURE  = 0.3;
            currentAgent.ACS.Parameters.FIXED_IRL_LEVEL_SELECTION_MEASURE = 0.0;
            currentAgent.ACS.Parameters.FIXED_RER_LEVEL_SELECTION_MEASURE = 0.0;

            Console.WriteLine("[ClarionAgent] ACS configured.");
        }

        #endregion

        #region Fixed Rule Support Calculators

        private double FixedRuleEat(ActivationCollection input, Rule target)
        {
            if (input.Contains(inputFoodClose, currentAgent.Parameters.MAX_ACTIVATION))
                return 1.0;
            return 0.0;
        }

        private double FixedRuleMoveToFood(ActivationCollection input, Rule target)
        {
            if (input.Contains(inputEnergyLow,  currentAgent.Parameters.MAX_ACTIVATION) &&
                input.Contains(inputFoodVisible, currentAgent.Parameters.MAX_ACTIVATION) &&
                !input.Contains(inputFoodClose,  currentAgent.Parameters.MAX_ACTIVATION))
                return 1.0;
            return 0.0;
        }

        private double FixedRuleAvoidWall(ActivationCollection input, Rule target)
        {
            if (input.Contains(inputWallAhead, currentAgent.Parameters.MAX_ACTIVATION))
                return 1.0;
            return 0.0;
        }

        private double FixedRulePickUp(ActivationCollection input, Rule target)
        {
            if (input.Contains(inputGoalCollect, currentAgent.Parameters.MAX_ACTIVATION) &&
                input.Contains(inputJewelClose,  currentAgent.Parameters.MAX_ACTIVATION))
                return 1.0;
            return 0.0;
        }

        private double FixedRuleMoveToJewel(ActivationCollection input, Rule target)
        {
            if (input.Contains(inputGoalCollect,  currentAgent.Parameters.MAX_ACTIVATION) &&
                input.Contains(inputJewelVisible,  currentAgent.Parameters.MAX_ACTIVATION) &&
                !input.Contains(inputWallAhead,    currentAgent.Parameters.MAX_ACTIVATION))
                return 0.9;
            return 0.0;
        }

        private double FixedRuleGoToDepotAfterCollect(ActivationCollection input, Rule target)
        {
            if (input.Contains(inputDepotFound,      currentAgent.Parameters.MAX_ACTIVATION) &&
                input.Contains(inputGoalCollect,     currentAgent.Parameters.MAX_ACTIVATION) &&
                input.Contains(inputLeafletComplete, currentAgent.Parameters.MAX_ACTIVATION))
                return 1.0;
            return 0.0;
        }

        private double FixedRuleMoveToDepot(ActivationCollection input, Rule target)
        {
            if (input.Contains(inputDepotFound,  currentAgent.Parameters.MAX_ACTIVATION) &&
                input.Contains(inputGoalDeliver, currentAgent.Parameters.MAX_ACTIVATION) &&
                !input.Contains(inputAtDepot,    currentAgent.Parameters.MAX_ACTIVATION))
                return 1.0;
            return 0.0;
        }

        private double FixedRuleDeliver(ActivationCollection input, Rule target)
        {
            if (input.Contains(inputDepotFound,  currentAgent.Parameters.MAX_ACTIVATION) &&
                input.Contains(inputGoalDeliver, currentAgent.Parameters.MAX_ACTIVATION) &&
                input.Contains(inputAtDepot,     currentAgent.Parameters.MAX_ACTIVATION))
                return 1.0;
            return 0.0;
        }

        private double FixedRuleStop(ActivationCollection input, Rule target)
        {
            if (input.Contains(inputGoalDone, currentAgent.Parameters.MAX_ACTIVATION))
                return 1.0;
            return 0.0;
        }

        #endregion

        #region Sensory Processing

        private IList<Thing> ReadWorldState()
        {
            if (worldServer == null || !worldServer.IsConnected) return null;

            IList<Thing> things = null;
            try
            {
                things = worldServer.SendGetCreatureState(creatureName);
            }
            catch (Exception e)
            {
                Console.WriteLine("[ReadWorldState] Error, skipping cycle: " + e.Message);
                return null;
            }

            if (things == null) return null;

            Creature creature = null;
            foreach (Thing t in things)
                if (t.CategoryId == Thing.CATEGORY_CREATURE) { creature = (Creature)t; break; }

            if (creature != null)
            {
                creatureX     = creature.X1;
                creatureY     = creature.Y1;
                creatureFuel  = creature.Fuel;
                creaturePitch = (Math.PI / 180.0) * creature.Pitch;
                while (creaturePitch >  Math.PI) creaturePitch -= 2 * Math.PI;
                while (creaturePitch < -Math.PI) creaturePitch += 2 * Math.PI;

                int n = 0;
                foreach (Leaflet l in creature.getLeaflets()) { mind.updateLeaflet(n, l); n++; }
            }

            if (!depotFound)
            {
                foreach (Thing t in things)
                {
                    if (t.CategoryId == Thing.CATEGORY_DeliverySPOT)
                    {
                        depotX = t.comX; depotY = t.comY; depotFound = true;
                        Console.WriteLine("[ClarionAgent] Depot at ("
                            + depotX.ToString("F0") + ", " + depotY.ToString("F0") + ")");
                        break;
                    }
                }
            }

            Sack sack = worldServer.SendGetSack("0");
            mind.setBag(sack);

            return things;
        }

        private SensoryInformation BuildSensoryInformation(IList<Thing> things, Creature creature)
        {
            SensoryInformation si  = World.NewSensoryInformation(currentAgent);
            double MAX = currentAgent.Parameters.MAX_ACTIVATION;
            double MIN = currentAgent.Parameters.MIN_ACTIVATION;

            bool wallAhead = false;
            foreach (Thing t in things)
                if (t.CategoryId == Thing.CATEGORY_BRICK && t.DistanceToCreature <= DISTANCE_WALL)
                { wallAhead = true; break; }
            si.Add(inputWallAhead, wallAhead ? MAX : MIN);

            targetJewel = FindNearestNeededJewel(things, creature);
            bool jewelVisible = targetJewel != null;
            bool jewelClose   = jewelVisible && targetJewel.DistanceToCreature <= DISTANCE_PICK_UP;
            si.Add(inputJewelVisible, jewelVisible ? MAX : MIN);
            si.Add(inputJewelClose,   jewelClose   ? MAX : MIN);

            double dx = creatureX - depotX;
            double dy = creatureY - depotY;
            bool atDepot = depotFound && Math.Sqrt(dx * dx + dy * dy) <= DISTANCE_DELIVER;
            si.Add(inputAtDepot, atDepot ? MAX : MIN);

            si.Add(inputLeafletComplete, IsLeafletComplete(creature) ? MAX : MIN);

            bool energyLow = creatureFuel < FUEL_LOW_THRESHOLD;
            si.Add(inputEnergyLow, energyLow ? MAX : MIN);

            targetFood = FindNearestFood(things);
            bool foodVisible = targetFood != null;
            bool foodClose   = foodVisible && targetFood.DistanceToCreature <= DISTANCE_EAT;
            si.Add(inputFoodVisible, foodVisible ? MAX : MIN);
            si.Add(inputFoodClose,   foodClose   ? MAX : MIN);

            si.Add(inputGoalCollect, activeGoal == GOAL_COLLECT ? MAX : MIN);
            si.Add(inputGoalDeliver, activeGoal == GOAL_DELIVER ? MAX : MIN);
            si.Add(inputGoalDone,    activeGoal == GOAL_DONE    ? MAX : MIN);

            si.Add(inputDepotFound, depotFound ? MAX : MIN);

            return si;
        }

        private Thing FindNearestNeededJewel(IList<Thing> things, Creature creature)
        {
            Dictionary<string, int> required  = new Dictionary<string, int>();
            Dictionary<string, int> collected = new Dictionary<string, int>();

            foreach (Leaflet l in creature.getLeaflets())
            {
                foreach (LeafletItem li in l.items)
                {
                    if (!required.ContainsKey(li.itemKey))  required[li.itemKey]  = 0;
                    if (!collected.ContainsKey(li.itemKey)) collected[li.itemKey] = 0;

                    required[li.itemKey]  += li.totalNumber;
                    collected[li.itemKey]  = li.collected;
                }
            }

            Dictionary<string, int> needed = new Dictionary<string, int>();
            foreach (var kv in required)
            {
                int still = kv.Value - collected[kv.Key];
                if (still > 0) needed[kv.Key] = still;
            }

            if (needed.Count == 0) return null;

            Thing nearest = null;
            double minDist = double.MaxValue;
            foreach (Thing t in things)
                if (t.CategoryId == Thing.CATEGORY_JEWEL &&
                    t.Material != null &&
                    needed.ContainsKey(t.Material.Color) &&
                    t.DistanceToCreature < minDist)
                { minDist = t.DistanceToCreature; nearest = t; }

            return nearest;
        }

        private Thing FindNearestFood(IList<Thing> things)
        {
            Thing nearest = null;
            double minDist = double.MaxValue;
            foreach (Thing t in things)
            {
                bool isFood = t.CategoryId == Thing.CATEGORY_FOOD
                        || t.CategoryId == Thing.categoryPFOOD
                        || t.CategoryId == Thing.CATEGORY_NPFOOD;
                if (isFood && t.DistanceToCreature < minDist)
                { minDist = t.DistanceToCreature; nearest = t; }
            }
            return nearest;
        }

        // Returns true if at least one leaflet is fully collected
        private bool IsLeafletComplete(Creature creature)
        {
            foreach (Leaflet l in creature.getLeaflets())
            {
                bool leafletDone = true;
                foreach (LeafletItem li in l.items)
                {
                    if (li.collected < li.totalNumber)
                    {
                        leafletDone = false;
                        break;
                    }
                }
                if (leafletDone) return true;
            }
            return false;
        }

        // Returns true if any leaflet still has items to collect
        private bool HasPendingLeaflets(Creature creature)
        {
            foreach (Leaflet l in creature.getLeaflets())
                foreach (LeafletItem li in l.items)
                    if (li.collected < li.totalNumber) return true;
            return false;
        }

        #endregion

        #region Action Execution

        private void ExecuteAction(CreatureActions action, IList<Thing> things, Creature creature)
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            if (worldServer == null || !worldServer.IsConnected) return;

            switch (action)
            {
                case CreatureActions.ROTATE_CLOCKWISE:
                    worldServer.SendSetAngle(creatureId, 2, -2, 2);
                    break;

                case CreatureActions.GO_AHEAD:
                    worldServer.SendSetAngle(creatureId, NAV_SPEED, NAV_SPEED, creaturePitch);
                    break;

                case CreatureActions.MOVE_TO_JEWEL:
                    if (targetJewel != null)
                        worldServer.SendSetGoTo(creatureId, NAV_SPEED, NAV_SPEED,
                            targetJewel.comX, targetJewel.comY);
                    break;

                case CreatureActions.PICK_UP_JEWEL:
                    if (targetJewel != null)
                    {
                        worldServer.SendSackIt(creatureId, targetJewel.Name);
                        Console.WriteLine("[ClarionAgent] Picked up: " + targetJewel.Name
                            + " (" + targetJewel.Material.Color + ")");

                        IList<Thing> fresh = worldServer.SendGetCreatureState(creatureName);
                        Creature fc = null;
                        foreach (Thing t in fresh)
                            if (t.CategoryId == Thing.CATEGORY_CREATURE) { fc = (Creature)t; break; }
                        if (fc != null && IsLeafletComplete(fc) && depotFound)
                        {
                            Console.WriteLine("[ClarionAgent] Leaflet complete -> DELIVER");
                            activeGoal = GOAL_DELIVER;
                        }
                    }
                    break;

                case CreatureActions.MOVE_TO_DEPOT:
                    if (activeGoal == GOAL_COLLECT && IsLeafletComplete(creature) && depotFound)
                    {
                        activeGoal = GOAL_DELIVER;
                        Console.WriteLine("[ClarionAgent] Goal -> DELIVER");
                    }
                    if (depotFound)
                        worldServer.SendSetGoTo(creatureId, NAV_SPEED, NAV_SPEED, depotX, depotY);
                    else
                        worldServer.SendSetAngle(creatureId, 2, -2, 2);
                    break;

                case CreatureActions.DELIVER_JEWELS:
                    worldServer.SendStopCreature(creatureId);
                    foreach (Leaflet l in creature.getLeaflets())
                    {
                        bool complete = true;
                        foreach (LeafletItem li in l.items)
                            if (li.collected < li.totalNumber) { complete = false; break; }

                        if (complete)
                        {
                            String response = worldServer.SendDeliverLeaflet(creatureId, l.leafletID.ToString());
                            Console.WriteLine("[ClarionAgent] Delivered leaflet " + l.leafletID + ": " + response);
                        }
                    }

                    // Re-read state to check if any leaflets still need collection
                    IList<Thing> freshThings = worldServer.SendGetCreatureState(creatureName);
                    Creature freshCreature = null;
                    foreach (Thing t in freshThings)
                        if (t.CategoryId == Thing.CATEGORY_CREATURE) { freshCreature = (Creature)t; break; }

                    if (freshCreature != null && HasPendingLeaflets(freshCreature))
                    {
                        activeGoal = GOAL_COLLECT;
                        Console.WriteLine("[ClarionAgent] Still pending leaflets -> COLLECT");
                    }
                    else
                    {
                        activeGoal = GOAL_DONE;
                        Console.WriteLine("[ClarionAgent] All leaflets delivered -> DONE");
                    }
                    break;

                case CreatureActions.MOVE_TO_FOOD:
                    if (creatureFuel < FUEL_LOW_THRESHOLD && targetFood != null)
                    {
                        if (goalBeforeEating < 0) goalBeforeEating = activeGoal;
                        worldServer.SendSetGoTo(creatureId, NAV_SPEED, NAV_SPEED,
                            targetFood.comX, targetFood.comY);
                    }
                    break;

                case CreatureActions.STOP:
                    worldServer.SendStopCreature(creatureId);
                    Console.WriteLine("[ClarionAgent] Stopped. All done.");
                    MaxNumberOfCognitiveCycles = currentCognitiveCycle + 1;
                    break;

                case CreatureActions.DO_NOTHING:
                default:
                    break;
            }
        }

        #endregion

        #region Reinforcement Feedback

        private double ComputeFeedback(CreatureActions chosenAction)
        {
            if (chosenAction == CreatureActions.EAT_FOOD)
            {
                if (targetFood != null && targetFood.DistanceToCreature <= DISTANCE_EAT)
                    return 1.0;
                return 0.0;
            }

            if (chosenAction == CreatureActions.MOVE_TO_FOOD)
            {
                if (creatureFuel < FUEL_LOW_THRESHOLD && targetFood != null)
                    return 0.7;
                return 0.0;
            }

            if (creatureFuel < FUEL_LOW_THRESHOLD)
                return 0.0;

            if (activeGoal == GOAL_COLLECT)
            {
                if (chosenAction == CreatureActions.PICK_UP_JEWEL)    return 1.0;
                if (chosenAction == CreatureActions.MOVE_TO_JEWEL)    return 0.6;
                if (chosenAction == CreatureActions.ROTATE_CLOCKWISE) return 0.2;
                return 0.4;
            }
            if (activeGoal == GOAL_DELIVER)
            {
                if (chosenAction == CreatureActions.DELIVER_JEWELS) return 1.0;
                if (chosenAction == CreatureActions.MOVE_TO_DEPOT)  return 0.6;
                return 0.3;
            }
            if (activeGoal == GOAL_DONE)
            {
                if (chosenAction == CreatureActions.STOP) return 1.0;
            }
            return 0.1;
        }

        #endregion

        #region Cognitive Cycle

        private void CognitiveCycle(object obj)
        {
            Console.WriteLine("[ClarionAgent] Cognitive cycle started.");

            while (currentCognitiveCycle != MaxNumberOfCognitiveCycles)
            {
                IList<Thing> things = ReadWorldState();
                if (things == null) { Thread.Sleep(100); continue; }

                Creature creature = null;
                foreach (Thing t in things)
                    if (t.CategoryId == Thing.CATEGORY_CREATURE) { creature = (Creature)t; break; }
                if (creature == null) { Thread.Sleep(100); continue; }

                // Goal transition — before action selection
                if (activeGoal == GOAL_COLLECT && depotFound && IsLeafletComplete(creature))
                {
                    activeGoal = GOAL_DELIVER;
                    Console.WriteLine("[ClarionAgent] Goal transition -> DELIVER");
                }

                SensoryInformation si = BuildSensoryInformation(things, creature);

                // Reflex: food close — eat immediately
                if (targetFood != null && targetFood.DistanceToCreature <= DISTANCE_EAT)
                {
                    worldServer.SendEatIt(creatureId, targetFood.Name);
                    Console.WriteLine("[Reflex] Ate: " + targetFood.Name
                        + " | Fuel: " + creatureFuel.ToString("F0"));
                    if (goalBeforeEating >= 0) {
                        activeGoal = goalBeforeEating;
                        goalBeforeEating = -1;
                    }
                    Thread.Sleep(TimeBetweenCognitiveCycles);
                    currentCognitiveCycle++;
                    continue;
                }

                currentAgent.Perceive(si);

                ExternalActionChunk chosen = currentAgent.GetChosenExternalAction(si);
                string actionLabel = chosen.LabelAsIComparable.ToString();
                CreatureActions actionType = (CreatureActions)Enum.Parse(
                    typeof(CreatureActions), actionLabel, true);

                Console.WriteLine("[Cycle " + currentCognitiveCycle.ToString("F0") + "]"
                    + " Fuel: " + creatureFuel.ToString("F0")
                    + " | Goal: " + activeGoal
                    + " | Action: " + actionType);

                ExecuteAction(actionType, things, creature);

                currentAgent.ReceiveFeedback(si, ComputeFeedback(actionType));

                currentCognitiveCycle++;

                if (currentCognitiveCycle % SAVE_EVERY_CYCLES == 0)
                    SaveWeights();

                if (TimeBetweenCognitiveCycles > 0)
                    Thread.Sleep(TimeBetweenCognitiveCycles);
            }

            SaveWeights();
            Console.WriteLine("[ClarionAgent] Cognitive cycle ended.");
        }

        #endregion
    }
}