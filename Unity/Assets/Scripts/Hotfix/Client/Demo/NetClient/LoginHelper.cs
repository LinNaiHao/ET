namespace ET.Client
{
    public static class LoginHelper
    {
        public static async ETTask Login(Scene root, string account, string password)
        {
            //移除旧的消息发送组件，因为重新登录的时候，有些信息会发生改变
            root.RemoveComponent<ClientSenderCompnent>();
            // 添加新的消息发送组件
            ClientSenderCompnent clientSenderCompnent = root.AddComponent<ClientSenderCompnent>();

            // 发起登录
            
            long playerId = await clientSenderCompnent.LoginAsync(account, password);

            root.GetComponent<PlayerComponent>().MyId = playerId;
            
            await EventSystem.Instance.PublishAsync(root, new LoginFinish());
        }
    }
}