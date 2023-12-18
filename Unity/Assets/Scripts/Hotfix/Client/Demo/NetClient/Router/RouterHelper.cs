using System;
using System.Net;

namespace ET.Client
{
    public static partial class RouterHelper
    {
        // 注册router
        public static async ETTask<Session> CreateRouterSession(this NetComponent netComponent, IPEndPoint address, string account, string password)
        {
            //通过账号和密码以及随机数生成一个随机值
            uint localConn = (uint)(account.GetLongHashCode() ^ password.GetLongHashCode() ^ RandomGenerator.RandUInt32());
            //获得可用的路由地址
            (uint recvLocalConn, IPEndPoint routerAddress) = await GetRouterAddress(netComponent, address, localConn, 0);

            //路由连接不成功
            if (recvLocalConn == 0)
            {
                throw new Exception($"get router fail: {netComponent.Root().Id} {address}");
            }
            
            Log.Info($"get router: {recvLocalConn} {routerAddress}");

            //路由能用，建立Session管理连接
            Session routerSession = netComponent.Create(routerAddress, address, recvLocalConn);
            routerSession.AddComponent<PingComponent>();
            routerSession.AddComponent<RouterCheckComponent>();
            
            return routerSession;
        }
        
        /// <summary>
        /// 获得可用的路由
        /// </summary>
        /// <param name="netComponent"></param>
        /// <param name="address">指定服务的内网地址</param>
        /// <param name="localConn"></param>
        /// <param name="remoteConn"></param>
        /// <returns></returns>
        public static async ETTask<(uint, IPEndPoint)> GetRouterAddress(this NetComponent netComponent, IPEndPoint address, uint localConn, uint remoteConn)
        {
            Log.Info($"start get router address: {netComponent.Root().Id} {address} {localConn} {remoteConn}");
            //return (RandomHelper.RandUInt32(), address);
            RouterAddressComponent routerAddressComponent = netComponent.Root().GetComponent<RouterAddressComponent>();
            //获得一个路由的ip地址
            IPEndPoint routerInfo = routerAddressComponent.GetAddress();
            //尝试去与路由建立连接
            uint recvLocalConn = await netComponent.Connect(routerInfo, address, localConn, remoteConn);
            
            Log.Info($"finish get router address: {netComponent.Root().Id} {address} {localConn} {remoteConn} {recvLocalConn} {routerInfo}");
            return (recvLocalConn, routerInfo);
        }

        // 向router申请
        private static async ETTask<uint> Connect(this NetComponent netComponent, IPEndPoint routerAddress, IPEndPoint realAddress, uint localConn, uint remoteConn)
        {
            //判断是新的连接，还是重连
            //当已经与服务器建立连接，remoteConn会被赋值
            uint synFlag = remoteConn == 0? KcpProtocalType.RouterSYN : KcpProtocalType.RouterReconnectSYN;

            // 注意，session也以localConn作为id，所以这里不能用localConn作为id
            // 因为重连的时候，Session已经存在了，所以这里不能用相同的ID
            long id = (long)(((ulong)localConn << 32) | remoteConn);
            using RouterConnector routerConnector = netComponent.AddChildWithId<RouterConnector>(id);
            
            int count = 20;
            
            //最长的IPV6地址，用UTF-8编码，也不会超过（41+端口号长度10）* 4 ~= 200
            //用来保存两个地址 512足够了
            byte[] sendCache = new byte[512];

            uint connectId = RandomGenerator.RandUInt32();
            //synFlag的有效值，不会操作8位，只需要一个字节就可以。
            sendCache.WriteTo(0, synFlag);
            //写入本地连接令牌
            sendCache.WriteTo(1, localConn);
            //写入远端连接令牌
            sendCache.WriteTo(5, remoteConn);
            //写入本次连接的随机数作为key
            sendCache.WriteTo(9, connectId);

            byte[] addressBytes = realAddress.ToString().ToByteArray();
            //写入目标服务器内网地址
            Array.Copy(addressBytes, 0, sendCache, 13, addressBytes.Length);
            TimerComponent timerComponent = netComponent.Root().GetComponent<TimerComponent>();
            Log.Info($"router connect: {localConn} {remoteConn} {routerAddress} {realAddress}");

            long lastSendTimer = 0;

            while (true)
            {
                long timeNow = TimeInfo.Instance.ClientFrameTime();
                //每0.3秒会尝试连接，尝试20次。
                if (timeNow - lastSendTimer > 300)
                {
                    if (--count < 0)
                    {
                        Log.Error($"router connect timeout fail! {localConn} {remoteConn} {routerAddress} {realAddress}");
                        return 0;
                    }

                    lastSendTimer = timeNow;
                    // 发送
                    routerConnector.Connect(sendCache, 0, addressBytes.Length + 13, routerAddress);
                }

                await timerComponent.WaitFrameAsync();
                
                if (routerConnector.Flag == 0)
                {
                    continue;
                }

                return localConn;
            }
        }
    }
}