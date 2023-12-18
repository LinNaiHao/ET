using System;
using System.Net;

namespace ET.Client
{
    [FriendOf(typeof(RouterConnector))]
    [EntitySystemOf(typeof(RouterConnector))]
    public static partial class RouterConnectorSystem
    {
        [EntitySystem]
        private static void Awake(this RouterConnector self)
        {
            NetComponent netComponent = self.GetParent<NetComponent>();
            KService kService = (KService)netComponent.AService;
            //添加路由连接请求确认的回调
            kService.AddRouterAckCallback(self.Id, (flag) =>
            {
                self.Flag = flag;
            });
        }
        [EntitySystem]
        private static void Destroy(this RouterConnector self)
        {
            NetComponent netComponent = self.GetParent<NetComponent>();
            KService kService = (KService)netComponent.AService;
            //移除回调
            kService.RemoveRouterAckCallback(self.Id);
        }

        public static void Connect(this RouterConnector self, byte[] bytes, int index, int length, IPEndPoint ipEndPoint)
        {
            NetComponent netComponent = self.GetParent<NetComponent>();
            KService kService = (KService)netComponent.AService;
            kService.Transport.Send(bytes, index, length, ipEndPoint);
        }
    }
}