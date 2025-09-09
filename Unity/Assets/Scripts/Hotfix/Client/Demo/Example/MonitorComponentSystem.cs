namespace ET.Client
{
    [EntitySystemOf(typeof(MonitorComponent))]
    [FriendOfAttribute(typeof(ET.Client.MonitorComponent))]
    public static partial class MonitorComponentSystem
    {
        [EntitySystem]
        private static void Awake(this ET.Client.MonitorComponent self, float args2)
        {
            Log.Error($"Awake Monitor with Light Intensity: {args2}");
            self.LightIntensity = args2;

        }
        [EntitySystem]
        private static void Destroy(this ET.Client.MonitorComponent self)
        {

            Log.Error("Destroy Monitor");
        }

        public static void ChangeLightIntensity(this MonitorComponent self, float intensity)
        {
            self.LightIntensity = intensity;
            Log.Debug($"Change Monitor Light Intensity: {self.LightIntensity}");
            
        }
    }
}

