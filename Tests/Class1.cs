using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using Neo.SmartContract.Framework.Services.System;

namespace Neo.SmartContract
{
    public class TestContract : Framework.SmartContract
    {
        public static object Main(string operation, object[] args)
        {
            switch (operation)
            {
                case "str":
                    return concat((byte[])args[0]);
                case "log":
                    Runtime.Log((string)args[0]);
                    return true;
                case "witness":
                    byte[] pubKey = (byte[])args[0];

                    var isChecked = Runtime.CheckWitness(pubKey);
                    Runtime.Log(isChecked ? "Check witness did success with default" : "Check witness did fail with default");
                    return true;
            }

            return true;
        }

        private static string concat(byte[] pk)
        {
            return "owner" + (string)(object)pk;
        }

        private static long mult(byte[] a, byte[] b)
        {
            long a1 = (long)(object)a;
            long a2 = (long)(object)b;
            long c = a1 * a2;
            Runtime.Notify(c);

            return c;
        }
    }
}
