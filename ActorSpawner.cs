using System;
using System.Collections.Generic;
using System.Linq;
using Server.Core.Accounts;
using Server.Core.Data.DtoWrappers;
using Server.Core.Spawns;
using Server.Core.Storage.Utility;
using Server.Core.StorageActors.LootActor;
using Server.Core.Utility.Logging;
using Server.Core.Worlds;
using Vector3 = Server.Core.Networking.Generated.Vector3;

namespace Server.Core.Storage.Loot
{
    public class ServerLootActorSpawner
    {
        private readonly object _locker = new();
        private readonly InstanceServer _instanceServer;
        private readonly List<LootActor> _lootStorageActors = new();
        private readonly IdentifiersPool _lootActorsIds = new();
        private const int LOOTING_RADIUS = 1000;

        public ServerLootActorSpawner(InstanceServer instanceServer)
        {
            _instanceServer = instanceServer;
        }

        public void OnNpcDead(Spawn spawnNpc)
        {
            if (spawnNpc.Prototype.Dto.GlobalLootTableNames.Count <= 0 
                && spawnNpc.Prototype.Dto.UniqueLootTableNames.Count <= 0) return;

            var aggressors = new List<Character>();
            var battlePawns = spawnNpc.StateFlowController.AggroRegister.Aggressors;
            
            foreach (var battlePawn in battlePawns)
            {
                if (battlePawn is not Character aggressor) continue;
                aggressors.Add(aggressor);
            }

            CreateActor(aggressors, spawnNpc.Position, spawnNpc.Prototype);
        }

        private void CreateActor(List<Character> aggressors, Vector3 position, NpcWrapper npcWrapper)
        {
            lock (_locker)
            {
                if (aggressors == null || aggressors.Count == 0)
                {
                    Debug.Error($"Not found aggressors after death {npcWrapper?.Name}", Debug.Pane.Loot);
                    return;
                }
               
                var globalLootTables = Model.Instance.Data.LootTablesProvider.GetTables(npcWrapper.Dto.GlobalLootTableNames);
                var uniqueLootTables = Model.Instance.Data.LootTablesProvider.GetTables(npcWrapper.Dto.UniqueLootTableNames);
                
                var loot = new Dictionary<int, List<Item>>();
                
                foreach (var table in globalLootTables)
                {
                    var lootItems = _instanceServer.LootGenerator.GenerateLoot(table);

                    if (lootItems.Count > 0)
                    {
                        if (loot.TryGetValue(CONSTANTS.GENERAL_LOOT_OWNER_ID, out var items))
                        {
                            items.AddRange(lootItems);
                        }
                        else
                        {
                            loot.Add(CONSTANTS.GENERAL_LOOT_OWNER_ID, lootItems);
                        }
                    }
                }
                
                foreach (var aggressor in aggressors)
                {
                    foreach (var table in uniqueLootTables)
                    {
                        var lootItems = _instanceServer.LootGenerator.GenerateLoot(table);
                    
                        if (lootItems.Count > 0)
                        {
                            if (loot.TryGetValue(aggressor.SeenActorId, out var items))
                            {
                                items.AddRange(lootItems);
                            }
                            else
                            {
                                loot.Add(aggressor.SeenActorId, lootItems);
                            }
                        }
                    }
                }

                if (loot.Count == 0) return;

                var actor = new LootActor(_lootActorsIds.Get(), position, npcWrapper.Dto.LootConfigShortCode, DespawnActor);
                
                actor.CreateStorage(loot);

                var currentAggressors = !loot.ContainsKey(CONSTANTS.GENERAL_LOOT_OWNER_ID)
                    ? aggressors.FindAll(a => loot.ContainsKey(a.SeenActorId))
                    : aggressors;

                actor.SetAggressors(currentAggressors);

                _lootStorageActors.Add(actor);
                
                SpawnLootForAggressors(currentAggressors, actor);
            }
        }

        private void SpawnLootForAggressors(List<Character> characters, SeenActor actor)
        {
            lock (_locker)
            {
                foreach (var aggressor in characters)
                {
                    aggressor.Send(actor.SpawnPacket);
                }
            }
        }

        private void DespawnActor(LootActor actor)
        {
            lock (_locker)
            {
                foreach (var aggressor in actor.GetAggressors().ToList())
                {
                    aggressor.Send(actor.DespawnPacket);
                    actor.RemoveAggressor(aggressor);
                }
                
                _lootStorageActors.Remove(actor);
                _lootActorsIds.Release(actor.SeenActorId);
            }
        }
        
        public void SendSpawnActors(Character pawn)
        {
            lock (_locker)
            {
                foreach (var actor in _lootStorageActors)
                {
                    if (actor.ContainsAggressor(pawn.SeenActorId))
                    {
                        pawn.Send(actor.SpawnPacket);
                    }
                }
            }
        }

        public List<LootActor> GetActorsInRadius(Character owner)
        {
            lock (_locker)
            {
                return _lootStorageActors.Where(actor =>
                    actor.ContainsAggressor(owner.SeenActorId) &&
                    actor.Position.Distance(owner.Position) <= LOOTING_RADIUS).ToList();
            }
        }
    }
}