namespace ET.Client
{
    [EntitySystemOf(typeof(Computer))]
    public static partial class ComputerSystem
    {
        [EntitySystem]
        private static void Awake(this ET.Client.Computer self)
        {
            Log.Error("Awake Computer");
        }

        [EntitySystem]
        private static void Update(this ET.Client.Computer self)
        {
            Log.Error("Update Computer");
        }

        [EntitySystem]
        private static void Destroy(this ET.Client.Computer self)
        {
            Log.Error("Close Computer");
        }

        public static void Open(this Computer self)
        {
            Log.Error("Open Computer");
        }
    }
}