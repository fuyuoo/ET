namespace ET.Client
{
    [EntitySystemOf(typeof(ComputersComponent))]
    public static partial class ComputersComponentSystem
    {
        [EntitySystem]
        private static void Awake(this ET.Client.ComputersComponent self)
        {
            Log.Error("ComputersComponentSystem Awake");
        }

        [EntitySystem]
        private static void Destroy(this ET.Client.ComputersComponent self)
        {
            Log.Error("ComputersComponentSystem Destroy");
        }
    }
}