using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;

namespace Neo.SmartContract
{
    public class RentAnnouncementsListingContract : Framework.SmartContract
    {
        public class AnnouncementPayLoad
        {
            public byte[] key;
            public byte[] value;
        }

        private static readonly string roomAndOwnerStorageMapKey = "kRoomAndOwnerStorageMap";
        private static readonly string announcementsStorageMapKey = "kAnnouncementsStorageMapKey";

        public static object Main(string operation, object[] args)
        {
            if (Runtime.Trigger != TriggerType.Application)
            {
                return false;
            }

            switch (operation)
            {
                case "regsiterRoomAndOwner":
                    RegisterRoomAndOwner((byte[])args[0], (byte[])args[1]);
                    return true;
                case "createAnnouncement":
                    CreateAnnouncement((byte[])args[0], (byte[])args[1], (byte[])args[2], (byte[])args[3]);
                    return true;
                case "checkRoomAndOwner":
                    return CheckRoomAndOwner((byte[])args[0], (byte[])args[1]);
                case "checkAnnouncement":
                    return CheckAnnouncement((byte[])args[0]);
                case "getAnnouncementParameters":
                    return GetAnnouncementParameters((byte[])args[0]);
                case "checkStorage":
                    StorageMap m = Storage.CurrentContext.CreateMap((string)args[0]);
                    return m.Get((byte[])args[1]);
            }

            return 1;
        }

        public static void RegisterRoomAndOwner(byte[] roomHash, byte[] ownerPubKey)
        {
            if (!Runtime.CheckWitness(ownerPubKey))
            {
                Runtime.Log("CheckWitness Failed");
                return;
            }

            StorageMap roomAndOwner = Storage.CurrentContext.CreateMap(roomAndOwnerStorageMapKey);
            var key = ownerPubKey.Concat(roomHash);
            roomAndOwner.Put(key, 1);

            return;
        }

        public static void CreateAnnouncement(byte[] roomHash, byte[] ownerPubKey, byte[] payPeriodInDays, byte[] price)
        {
            if (!Runtime.CheckWitness(ownerPubKey))
            {
                Runtime.Log("CheckWitness Failed");
                return;
            }

            if (!CheckRoomAndOwner(roomHash, ownerPubKey))
            {
                Runtime.Log("CheckRoomAndOwner Failed");
                return;
            }

            StorageMap announcements = Storage.CurrentContext.CreateMap(announcementsStorageMapKey);
            AnnouncementPayLoad payload = MakeAnnouncementKeyAndValueFrom(roomHash, ownerPubKey, payPeriodInDays, price);
            Runtime.Notify(payload.key);

            announcements.Put(payload.key, payload.value);

            return;
        }

        public static bool CheckRoomAndOwner(byte[] roomHash, byte[] ownerPubKey)
        {
            StorageMap roomAndOwnerMap = Storage.CurrentContext.CreateMap(roomAndOwnerStorageMapKey);
            var exist = roomAndOwnerMap.Get(ownerPubKey.Concat(roomHash)).Length > 0;
            Runtime.Notify(exist);

            return exist;
        }

        public static bool CheckAnnouncement(byte[] announcementHash)
        {
            StorageMap announcements = Storage.CurrentContext.CreateMap(announcementsStorageMapKey);
            bool exist = announcements.Get(announcementHash).Length > 0;
            Runtime.Notify(exist);

            return announcements.Get(announcementHash).Length > 0;
        }

        private static AnnouncementPayLoad MakeAnnouncementKeyAndValueFrom(byte[] roomHash, byte[] ownerPubKey, byte[] payPeriodInDays, byte[] price)
        {
            byte[] key = Sha256(roomHash.Concat(ownerPubKey).Concat(payPeriodInDays).Concat(price));
            object[] parameters = new object[] { roomHash, ownerPubKey, payPeriodInDays, price };
            AnnouncementPayLoad payload = new AnnouncementPayLoad();
            payload.key = key;
            payload.value = parameters.Serialize();

            return payload;
        }

        public static object[] GetAnnouncementParameters(byte[] announcementHash)
        {
            Runtime.Log("Called successfully");
            StorageMap announcements = Storage.CurrentContext.CreateMap(announcementsStorageMapKey);
            byte[] parametersByteArray = announcements.Get(announcementHash);
            object[] parms = (object[])parametersByteArray.Deserialize();

            foreach (object o in parms)
            {
                Runtime.Notify(o);
            }

            return parms;
        }
    }
}
