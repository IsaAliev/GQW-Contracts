using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System.Numerics;
using Neo.SmartContract.Framework.Services.System;

namespace Neo.SmartContract
{
    public class RentContractCreatorContract : Framework.SmartContract
    {
        // add getting req hashes and concrete req

        [Appcall("0d629cfe2e8e41365de762e67e5893b8134ddfa4")]
        public static extern object[] AnnouncementsListing(string operation, object[] args);


        [Appcall("5fdf268d58ff84bd32444051e5492101b3ab8458")]
        public static extern byte[] RentContract(string operation, object[] args);

        private static readonly string allowedClaimsStorageMapKey = "kAllowedClaimsStorageMapKey";

        private static readonly int announcementHashIdxInRequest = 0;
        private static readonly int tenantPubKeyIdxInRequest = 1;
        private static readonly int requestIDIdxInRequest = 2;
        private static readonly int termIdxInRequest = 3;
        private static readonly int txHashIdxInRequest = 4;

        private static readonly int roomIdxInAnnouncementParams = 0;
        private static readonly int ownerIdxInAnnouncementParams = 1;
        private static readonly int payPeriodIdxInAnnouncementParams = 2;
        private static readonly int priceIdxInAnnouncementParams = 3;

        public static object Main(string operation, object[] args)
        {

            if (Runtime.Trigger == TriggerType.Application)
            {
                switch (operation)
                {
                    case "acceptRequest":
                        return AcceptRequest((byte[])args[0], (byte[])args[1]);
                    case "createRequest":
                        return CreateRequest((byte[])args[0], (byte[])args[1], (byte[])args[2]);
                    case "checkStorage":
                        StorageMap map = RequestsMapForOwner((byte[])args[0]);
                        Runtime.Notify(map.Get((byte[])args[1]));
                        return map.Get((byte[])args[1]);
                    case "checkRequest":
                        return GetRequest((byte[])args[0], (byte[])args[1]);
                    case "deleteRequest":
                        DeleteRequest((byte[])args[0], (byte[])args[1]);
                        return true;
                    case "checkClaim":
                        return Storage.CurrentContext.CreateMap(allowedClaimsStorageMapKey).Get((byte[])args[0]);
                    case "deleteClaim":
                        Storage.CurrentContext.CreateMap(allowedClaimsStorageMapKey).Delete((byte[])args[0]);
                        return true;
                }

                return false;
            }

            if (Runtime.Trigger == TriggerType.Verification)
            {
                Runtime.Log("Verification Triggered");
                bool isValid = false;

                Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
                var contractHash = ExecutionEngine.ExecutingScriptHash;
                byte[] outputHash = null;

                foreach (TransactionOutput output in tx.GetOutputs())
                {
                    var remitee = output.ScriptHash;

                    if (remitee == contractHash)
                    {
                        return false;
                    }

                    if (outputHash == null)
                    {
                        outputHash = remitee;
                    } else if (remitee != outputHash)
                    {
                        return false;
                    }
                }

                foreach (TransactionInput input in tx.GetInputs())
                {
                    var hash = input.PrevHash;
                    byte[] claimable = GetClaimable(hash);

                    isValid = Runtime.CheckWitness(claimable);
                }

                Runtime.Log(isValid ? "Allowed" : "Not Allowed");

                return isValid;
            }

            return false;
        }

        // Partially tested
        public static bool AcceptRequest(byte[] requestID, byte[] ownerPubKey)
        {
            if (!Runtime.CheckWitness(ownerPubKey))
            {
                Runtime.Log("Check witness failed");
                return false;
            }

            object[] request = GetRequest(ownerPubKey, requestID);

            byte[] announcementHash = (byte[])request[announcementHashIdxInRequest];
            object[] args = new object[] { announcementHash };
            object[] announcementParams = AnnouncementsListing("getAnnouncementParameters", args);
            byte[] price = (byte[])announcementParams[priceIdxInAnnouncementParams];
            StorageContext ctx = Storage.CurrentContext;

            object[] contractArgs = new object[] { ownerPubKey,
                (byte[])request[tenantPubKeyIdxInRequest],
                (byte[])announcementParams[roomIdxInAnnouncementParams],
                price,
                (byte[])announcementParams[payPeriodIdxInAnnouncementParams],
                (byte[])request[termIdxInRequest] };

            byte[] contractHash = RentContract("create", contractArgs);

            AllowClaim((byte[])request[txHashIdxInRequest], ownerPubKey);
            DeleteRequest(ownerPubKey, requestID);
            Runtime.Log("OK");
            Runtime.Notify(contractHash);

            return true;
        }

        // Tested
        public static bool CreateRequest(byte[] announcementHash, byte[] pubKey, byte[] termInDays)
        {
            if (!Runtime.CheckWitness(pubKey))
            {
                Runtime.Log("Check witness failed");
                return false;
            }

            object[] args = new object[] { announcementHash };
            object[] announcementParams = AnnouncementsListing("getAnnouncementParameters", args);
            byte[] ownerPubKey = (byte[])announcementParams[ownerIdxInAnnouncementParams];
            byte[] requestID = announcementHash.Concat(pubKey);

            if (RequestExists(ownerPubKey, requestID))
            {
                Runtime.Log("Request already exists");
                return false;
            }

            byte[] price = (byte[])announcementParams[priceIdxInAnnouncementParams];

            Runtime.Log("Try deposit");
            BigInteger cprice = price.AsBigInteger();
            BigInteger depositSum = Deposit();

            if (depositSum < cprice)
            {
                AllowClaim(((Transaction)ExecutionEngine.ScriptContainer).Hash, pubKey);
                Runtime.Log("Insufficent coins deposited");
                return false;
            }

            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            object[] request = new object[] { announcementHash, pubKey, requestID, termInDays, tx.Hash };
            StoreNewRequest(ownerPubKey, request);

            Runtime.Log("Succesfully created request");

            return true;
        }

        private static void StoreNewRequest(byte[] owner, object[] request) {
            StorageMap reqsMap = RequestsMapForOwner(owner);
            byte[] currentRequestsByteArray = reqsMap.Get("kRequestsHashes");
            object[] currentRequests = new object[0];

            if (currentRequestsByteArray.Length > 0)
            {
                currentRequests = (object[])currentRequestsByteArray.Deserialize();
            }

            Runtime.Notify((byte[])request[requestIDIdxInRequest]);

            reqsMap.Put("kRequestsHashes", AddItemToArray(request[requestIDIdxInRequest], currentRequests).Serialize());
            reqsMap.Put((byte[])request[requestIDIdxInRequest], request.Serialize());
        }

        private static object[] GetRequest(byte[] owner, byte[] requestID)
        {
            return (object[])RequestsMapForOwner(owner).Get(requestID).Deserialize(); ;
        }

        private static void DeleteRequest(byte[] owner, byte[] requestID)
        {
            StorageMap reqsMap = RequestsMapForOwner(owner);
            object[] request = (object[])reqsMap.Get(requestID).Deserialize();
            byte[] tenantPK = (byte[])request[tenantPubKeyIdxInRequest];

            bool isTenant = Runtime.CheckWitness(tenantPK);
            bool isOwner = Runtime.CheckWitness(owner);

            if (!isTenant && !isOwner)
            {
                return;
            }

            object[] currentRequestsHashes = (object[])reqsMap.Get("kRequestsHashes").Deserialize();
            object[] newHashes = new object[currentRequestsHashes.Length - 1];
            int deletingReqHashIdx = currentRequestsHashes.Length - 1;

            for (int i = 0; i < currentRequestsHashes.Length; i++) {
                if ((byte[])currentRequestsHashes[i] == requestID)
                {
                    deletingReqHashIdx = i;
                    continue;
                }

                var idxToWrite = i;
                if (i > deletingReqHashIdx)
                {
                    idxToWrite = i - 1;
                }

                newHashes[idxToWrite] = currentRequestsHashes[i];
            }

            reqsMap.Delete(requestID);
            if (isTenant)
            {
                AllowClaim((byte[])request[txHashIdxInRequest], tenantPK);
            }
        }

        private static bool RequestExists(byte[] owner, byte[] hash)
        {
            StorageMap reqsMap = RequestsMapForOwner(owner);
            return reqsMap.Get(hash).Length > 0;
        }

        private static object[] AddItemToArray(object item, object[] array)
        {
            long newLength = array.Length + 1;
            object[] newArray = new object[newLength];
            for (int i = 0; i < array.Length; i++) {
                newArray[i] = array[i];
            }
            newArray[newLength - 1] = item;

            return newArray;
        }

        private static StorageMap RequestsMapForOwner(byte[] owner)
        {
            return Storage.CurrentContext.CreateMap("requests_" + (string)(object)owner);
        }

        private static void AllowClaim(byte[] txHash, byte[] allowedPubKey)
        {
            StorageMap allowedClaims = Storage.CurrentContext.CreateMap(allowedClaimsStorageMapKey);
            Runtime.Log("Allowing claim..");
            
            allowedClaims.Put(txHash, allowedPubKey);
        }

        private static byte[] GetClaimable(byte[] txHash) {
            StorageMap allowedClaims = Storage.CurrentContext.CreateMap(allowedClaimsStorageMapKey);

            return allowedClaims.Get(txHash);
        }

        private static BigInteger Deposit()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            var references = tx.GetReferences();

            if (references.Length < 1) {
                Runtime.Log("No refs");
                return 0;
            }
            
            var contractHash = ExecutionEngine.ExecutingScriptHash;
            BigInteger outputSum = 0;

            foreach (TransactionOutput output in tx.GetOutputs()) {
                var remitee = output.ScriptHash;

                if (contractHash != remitee)
                {
                    continue;
                }

                var value = output.Value;
                outputSum = outputSum + value;
            }

            return outputSum;
        }
    }
}