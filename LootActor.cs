using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Server.Core.Accounts;
using Server.Core.Location;
using Server.Core.Networking.Generated;
using Server.Core.Spawns;
using Server.Core.Storage.Inventories;
using Server.Core.Storage.Utility;
using Server.Core.Utility;
using Vector3 = Server.Core.Networking.Generated.Vector3;

namespace Server.Core.StorageActors.LootActor
{
    public class LootActor : SeenActor
    {
        public event Action<LootActor> OnActorDespawn;
        public LootActorInventory Storage { get; private set; }
        
        private readonly Action<LootActor> _removeCallback;
        private List<Character> _allAggressors;
        private CallbackWrapper _despawnTimer;
        private const int DESPAWN_TIMER_VALUE = 60000;
        private readonly string _shortCode;
      
        public override IMessage SpawnPacket
        {
            get
            {
                var message = new PckSpawnLootActor
                {
                    LootId = SeenActorId,
                    Position = Position,
                    ShortCode = _shortCode
                };

                return message;
            }
        }

        public override IMessage DespawnPacket
        {
            get
            {
                var message = new PckDespawnLootActor
                {
                    ActorId = SeenActorId
                };
                
                return message;
            }
        }
        
        public LootActor(IdentifiersPool.Identifier id, Vector3 position, string confShortCode, Action<LootActor> removeCallback)
        {
            SeenActorId = id;
            Position = position;
            SeenList = new PawnSeenList(this);

            _shortCode = confShortCode;
            _removeCallback = removeCallback;
        }
        
        public void CreateStorage(Dictionary<int, List<Item>> loot)
        {
            Storage = new LootActorInventory(this, loot);

            _despawnTimer = Model.Instance.Heart.CallOnce(RemoveActor, DESPAWN_TIMER_VALUE, false);
        }

        public void SetAggressors(List<Character> aggressors)
        {
            _allAggressors = aggressors;
        }

        public List<Character> GetAggressors()
        {
            return _allAggressors;
        }
        
        public bool ContainsAggressor(int seenId)
        {
            return _allAggressors.Any(actor => actor.SeenActorId == seenId);
        }

        public void RemoveAggressor(Character aggressor)
        {
            _allAggressors.Remove(aggressor);
        }

        public void RemoveActor()
        {
            _despawnTimer?.Cancel();

            OnActorDespawn?.Invoke(this);

            _removeCallback?.Invoke(this);
        }
    }
}