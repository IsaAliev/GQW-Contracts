using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System.Numerics;
using Neo.SmartContract.Framework.Services.System;

namespace Neo.SmartContract
{
    public class RentContract : Framework.SmartContract
    {
        private static readonly int timestampKey = 0;
        private static readonly int ownerKey = 1;
        private static readonly int tenantKey = 2;
        private static readonly int roomKey = 3;
        private static readonly int payPeriodKey = 4;
        private static readonly int priceKey = 5;
        private static readonly int termKey = 6;
        private static readonly int paidPeriodsCountKey = 7;
        private static readonly int terminatedKey = 8;
        private static readonly int warnedTerminationTimestampKey = 9;
        private static readonly int contractPropsCount = 10;

        private static readonly string terminationRequestsKey = "terminationRequestsKey";

        public static object Main(string operation, object[] args)
        {

            if (Runtime.Trigger == TriggerType.Application)
            {
                switch (operation)
                {
                    case "create":
                        return Create((byte[])args[0], (byte[])args[1], (byte[])args[2], (byte[])args[3], (byte[])args[4], (byte[])args[5]);
                    case "pay":
                        return Pay((byte[])args[0]);
                    case "getInfo":
                        return GetInfo((byte[])args[0]);
                    case "getTxsToClaim":
                        return GetTxsToClaim((byte[])args[0]);
                    case "terminate":
                        Terminate((byte[])args[0]);
                        return 0;
                    case "warnTermination":
                        return WarnTermination((byte[])args[0]);
                    case "confirmTermination":
                        ConfirmTermination((byte[])args[0]);
                        return 0;
                    case "getTerminationRequests":
                        return GetTerminationRequests();
                    default:
                        return false;
                }
            }

            if (Runtime.Trigger == TriggerType.Verification)
            {
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
                    }
                    else if (remitee != outputHash)
                    {
                        return false;
                    }
                }

                foreach (TransactionInput input in tx.GetInputs())
                {
                    var hash = input.PrevHash;
                    byte[] keyAllowedToClaim = Storage.Get(hash);

                    isValid = Runtime.CheckWitness(keyAllowedToClaim);
                }

                Runtime.Log(isValid ? "Allowed" : "Not Allowed");

                return isValid;
            }

            return true;
        }

        public static bool WarnTermination(byte[] contractHash)
        {
            object[] contract = (object[])Storage.Get(contractHash).Deserialize();
            BigInteger term = ((byte[])contract[termKey]).AsBigInteger();

            if (!Runtime.CheckWitness((byte[])contract[tenantKey]) && !Runtime.CheckWitness((byte[])contract[ownerKey]))
            {
                return false;
            }

            if (term > 0)
            {
                return false;
            }

            contract[warnedTerminationTimestampKey] = GetTimestamp().AsByteArray();
            Storage.Put(contractHash, contract.Serialize());

            return true;
        }

        public static object[] GetTerminationRequests()
        {
            return (object[])Storage.Get(terminationRequestsKey).Deserialize();
        }

        public static void ConfirmTermination(byte[] contractHash)
        {
            // CheckWitness system creator

            object[] contract = (object[])Storage.Get(contractHash).Deserialize();
            contract[terminatedKey] = 1;
            Storage.Put(contractHash, contract.Serialize());
        }

        public static void Terminate(byte[] contractHash)
        {
            object[] contract = (object[])Storage.Get(contractHash).Deserialize();
            byte[] owner = (byte[])contract[ownerKey];
            byte[] tenant = (byte[])contract[tenantKey];
            bool isTenant = Runtime.CheckWitness(tenant);
            bool isOwner = Runtime.CheckWitness(owner);

            byte[] warningTimestampByteArray = (byte[])contract[warnedTerminationTimestampKey];

            if (warningTimestampByteArray.Length > 0)
            {
                BigInteger warningTimestamp = warningTimestampByteArray.AsBigInteger();
                BigInteger threeMonthsInSecs = 24 * 60 * 60 * 31 * 3;
                if (warningTimestamp + threeMonthsInSecs < GetTimestamp())
                {
                    contract[terminatedKey] = 1;
                    Storage.Put(contractHash, contract.Serialize());
                    return;
                }
            }

            if (isOwner)
            {
                BigInteger periodsPaid = ((byte[])contract[paidPeriodsCountKey]).AsBigInteger();
                BigInteger time = GetTimestamp();
                BigInteger periodInDays = ((byte[])contract[payPeriodKey]).AsBigInteger();
                BigInteger timeContractCreated = ((byte[])contract[timestampKey]).AsBigInteger();
                BigInteger timePassed = time - timeContractCreated;
                BigInteger passedInDays = timePassed / (24 * 60 * 60);
                BigInteger periodsMustBePaid = passedInDays / periodInDays + 1;

                if (periodsMustBePaid - periodsPaid > 1)
                {
                    contract[terminatedKey] = 1;
                    Storage.Put(contractHash, contract.Serialize());
                    return;
                }
            }

            if (!isTenant && !isOwner)
            {
                return;
            }

            byte[] terminationsByteArray = Storage.Get(terminationRequestsKey);
            if (terminationsByteArray.Length == 0)
            {
                Storage.Put(terminationRequestsKey, (new object[] { contractHash }).Serialize());
                return;
            }

            var array = terminationsByteArray.Deserialize();
            Storage.Put(terminationRequestsKey, AddItemToArray(contractHash, (object[])array).Serialize());
        }

        public static byte[] GetTxsToClaim(byte[] pubKey)
        {
            return Storage.Get(pubKey);
        }

        public static object[] GetInfo(byte[] contractHash)
        {
            object[] contract = (object[])Storage.Get(contractHash).Deserialize();
            BigInteger time = GetTimestamp();
            BigInteger timeContractCreated = ((byte[])contract[timestampKey]).AsBigInteger();
            BigInteger timePassed = time - timeContractCreated;
            BigInteger periodsPaid = ((byte[])contract[paidPeriodsCountKey]).AsBigInteger();
            BigInteger periodInDays = ((byte[])contract[payPeriodKey]).AsBigInteger();
            bool isPaid = periodInDays * periodsPaid * 24 * 60 * 60 > timePassed;

            return AddItemToArray((isPaid ? new byte[] { 01 } : new byte[0]), contract);
        }

        public static bool Pay(byte[] contractHash)
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            object[] contract = (object[])Storage.Get(contractHash).Deserialize();
            BigInteger price = ((byte[])contract[priceKey]).AsBigInteger();
            byte[] ownerPK = (byte[])contract[ownerKey];
            byte[] tenantPK = (byte[])contract[tenantKey];

            if (Deposit() < price)
            {
                Storage.Put(tx.Hash, tenantPK);
                Runtime.Log("Insufficient coins");
                return false;
            }

            Runtime.Log("Deposited");
            BigInteger count = ((byte[])contract[paidPeriodsCountKey]).AsBigInteger() + 1;
            contract[paidPeriodsCountKey] = count.AsByteArray();

            Storage.Put(contractHash, contract.Serialize());
            Storage.Put(tx.Hash, ownerPK);
            byte[] claimableTxsByteArray = Storage.Get(ownerPK);

            if (claimableTxsByteArray.Length == 0)
            {
                Storage.Put(ownerPK, (new object[] { tx.Hash }).Serialize());
            } else
            {
                var array = claimableTxsByteArray.Deserialize();
                Storage.Put(ownerPK, AddItemToArray(tx.Hash, (object[])array).Serialize());
            }

            Storage.Put(tx.Hash, ownerPK);
            Runtime.Log("OK");

            return true;
        }

        public static byte[] Create(byte[] ownerPubKey, byte[] tenantPubKey,
            byte[] roomHash, byte[] price,
            byte[] payPeriodInDays, byte[] termInDays)
        {
            Runtime.Log("Contract creation triggered");
            byte[] contractHash = Sha256(ownerPubKey.Concat(tenantPubKey).Concat(roomHash));
            object[] contractProps = new object[contractPropsCount];

            contractProps[timestampKey] = GetTimestamp().AsByteArray();
            contractProps[ownerKey] = ownerPubKey;
            contractProps[tenantKey] = tenantPubKey;
            contractProps[roomKey] = roomHash;
            contractProps[payPeriodKey] = payPeriodInDays;
            contractProps[priceKey] = price;
            contractProps[termKey] = termInDays;
            contractProps[paidPeriodsCountKey] = (new BigInteger(1)).AsByteArray();
            contractProps[terminatedKey] = new byte[0];
            contractProps[warnedTerminationTimestampKey] = new byte[0];

            Storage.Put(contractHash, contractProps.Serialize());
            Runtime.Log("Contract created");

            return contractHash;
        }

        private static BigInteger GetTimestamp()
        {
            uint h = Blockchain.GetHeight();
            return Blockchain.GetHeader(h).Timestamp;
        }

        private static long Deposit()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            var references = tx.GetReferences();

            if (references.Length < 1)
            {
                Runtime.Log("No refs");
                return 0;
            }

            var contractHash = ExecutionEngine.ExecutingScriptHash;
            long outputSum = 0;

            foreach (TransactionOutput output in tx.GetOutputs())
            {
                Runtime.Log("Checking output");
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

        private static object[] AddItemToArray(object item, object[] array)
        {
            long newLength = array.Length + 1;
            object[] newArray = new object[newLength];
            for (int i = 0; i < array.Length; i++)
            {
                newArray[i] = array[i];
            }
            newArray[newLength - 1] = item;

            return newArray;
        }
    }
}
