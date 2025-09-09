namespace ET.Client
{
    [ComponentOf(typeof(Computer))]
    public class MonitorComponent : Entity, IAwake<float>, IDestroy
    {
        public float LightIntensity;
    }
}

