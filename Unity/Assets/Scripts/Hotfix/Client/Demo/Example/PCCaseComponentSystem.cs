namespace ET.Client
{
    [EntitySystemOf(typeof(PCCaseComponent))]
    public static partial class PCCaseComponentSystem
    {
        [EntitySystem]
        private static void Awake(this ET.Client.PCCaseComponent self)
        {
            Log.Error("Awake PCCase");
        }
    }
}