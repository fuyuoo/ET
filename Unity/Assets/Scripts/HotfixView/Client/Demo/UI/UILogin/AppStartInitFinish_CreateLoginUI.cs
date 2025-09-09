namespace ET.Client
{
    [Event(SceneType.Demo)]
    public class AppStartInitFinish_CreateLoginUI : AEvent<Scene, AppStartInitFinish>
    {
        protected override async ETTask Run(Scene root, AppStartInitFinish args)
        {
            await UIHelper.Create(root, UIType.UILogin, UILayer.Mid);
            Computer computer = root.GetComponent<ComputersComponent>().AddChild<Computer>();
            computer.AddComponent<MonitorComponent, float>(10);
            computer.AddComponent<PCCaseComponent>();
            computer.Open();
            computer.GetComponent<MonitorComponent>().ChangeLightIntensity(20);
            await root.GetComponent<TimerComponent>().WaitAsync(3000);
            computer.Dispose();
        }
    }
}