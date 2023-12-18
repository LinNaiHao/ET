using System.Collections.Generic;

namespace ET
{
    public enum TimerClass
    {
        None,
        // 执行一次，会通过EventSystem.Invoke 触发 
        // wait时间长不需要逻辑连贯的建议用该类型
        OnceTimer,
        // 等待时间结束后回调，通过关联了一个ETTask实现
        // wait时间短并且逻辑需要连贯的建议使用该类型
        OnceWaitTimer,
        // 按时间间隔重复执行，会通过EventSystem.Invoke 触发
        RepeatedTimer,
    }

    public struct TimerAction
    {
        public TimerAction(TimerClass timerClass, long startTime, long time, int type, object obj)
        {
            this.TimerClass = timerClass;
            this.StartTime = startTime;
            this.Object = obj;
            this.Time = time;
            this.Type = type;
        }
        // 计时的类型
        public TimerClass TimerClass;
        // 任务的类型
        public int Type;

        //绑定对象
        public object Object;
        //开始的时间
        public long StartTime;
        //等待的时间
        public long Time;
    }

    // 倒计时任务结束时会通过EventSystem.Invoke抛出该事件。
    public struct TimerCallback
    {
        public object Args;
    }

    [EntitySystemOf(typeof(TimerComponent))]
    public static partial class TimerComponentSystem
    {
        [EntitySystem]
        private static void Awake(this TimerComponent self)
        {
        }
        
        [EntitySystem]
        private static void Update(this TimerComponent self)
        {
            if (self.timeId.Count == 0)
            {
                return;
            }

            //判断当前时间与最近的一个计时任务，如果时间还没到，则直接返回
            //这里用的时间是与服务器对比矫正后的时间
            long timeNow = self.GetNow();

            if (timeNow < self.minTime)
            {
                return;
            }

            foreach (var kv in self.timeId)
            {
                long k = kv.Key;
                //找到第一个还没到的计时任务，记录为最近的时间点，跳出循环
                if (k > timeNow)
                {
                    self.minTime = k;
                    break;
                }
                //将完成了的计时任务的key存入列表
                self.timeOutTime.Enqueue(k);
            }
            
            //收集所有完成了的计时任务
            while (self.timeOutTime.Count > 0)
            {
                long time = self.timeOutTime.Dequeue();
                var list = self.timeId[time];
                for (int i = 0; i < list.Length; ++i)
                {
                    long timerId = list[i];
                    self.timeOutTimerIds.Enqueue(timerId);
                }
                //移除timerId对应的数据
                self.timeId.Remove(time);
            }

            //如果计时任务列表空了，则暂停Update的逻辑
            //下一个添加的任务，将必定比long.Maxvalue小，就会更新minTime
            if (self.timeId.Count == 0)
            {
                self.minTime = long.MaxValue;
            }

            while (self.timeOutTimerIds.Count > 0)
            {
                long timerId = self.timeOutTimerIds.Dequeue();

                if (!self.timerActions.Remove(timerId, out TimerAction timerAction))
                {
                    continue;
                }
                //取出对应id的计时任务，处理任务
                self.Run(timerId, ref timerAction);
            }
        }
        
        private static long GetId(this TimerComponent self)
        {
            return ++self.idGenerator;
        }

        private static long GetNow(this TimerComponent self)
        {
            return TimeInfo.Instance.ServerFrameTime();
        }

        private static void Run(this TimerComponent self, long timerId, ref TimerAction timerAction)
        {
            switch (timerAction.TimerClass)
            {
                case TimerClass.OnceTimer:
                {
                    //直接抛出计时任务完成的事件，接受方通过timerActino.Type来区分处理句柄
                    EventSystem.Instance.Invoke(timerAction.Type, new TimerCallback() { Args = timerAction.Object });
                    break;
                }
                case TimerClass.OnceWaitTimer:
                {
                    //完成等待Task，使调用逻辑处的异步等待能继续执行下去
                    ETTask tcs = timerAction.Object as ETTask;
                    tcs.SetResult();
                    break;
                }
                case TimerClass.RepeatedTimer:
                {                    
                    //重新添加相同的计时任务
                    long timeNow = self.GetNow();
                    timerAction.StartTime = timeNow;
                    self.AddTimer(timerId, ref timerAction);
                    //直接抛出计时任务完成的事件，接受方通过timerActino.Type来区分处理句柄
                    EventSystem.Instance.Invoke(timerAction.Type, new TimerCallback() { Args = timerAction.Object });
                    break;
                }
            }
        }

        private static void AddTimer(this TimerComponent self, long timerId, ref TimerAction timer)
        {
            //计算完成任务的时间
            long tillTime = timer.StartTime + timer.Time;
            //添加到时间轴内
            self.timeId.Add(tillTime, timerId);
            //添加到对应的计时任务容器中
            self.timerActions.Add(timerId, timer);
            //如果该任务比当前所有的任务都要早完成，则更新minTime
            if (tillTime < self.minTime)
            {
                self.minTime = tillTime;
            }
        }

        public static bool Remove(this TimerComponent self, ref long id)
        {
            long i = id;
            //将任务移除了，则对任务id的引用也要清空
            id = 0;
            //返回是否移除成功
            return self.Remove(i);
        }

        private static bool Remove(this TimerComponent self, long id)
        {
            if (id == 0)
            {
                return false;
            }

            if (!self.timerActions.Remove(id, out TimerAction _))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 异步等待一段时间
        /// 使用方法有点类似协程里的。yield return new WaitForSeconds(tillTime - timeNow)
        /// </summary>
        /// <param name="self"></param>
        /// <param name="tillTime">持续到什么时候</param>
        /// <param name="cancellationToken">打断该计时器的令牌</param>
        public static async ETTask WaitTillAsync(this TimerComponent self, long tillTime, ETCancellationToken cancellationToken = null)
        {
            long timeNow = self.GetNow();
            if (timeNow >= tillTime)
            {
                return;
            }

            ETTask tcs = ETTask.Create(true);
            long timerId = self.GetId();
            TimerAction timer = new(TimerClass.OnceWaitTimer, timeNow, tillTime - timeNow, 0, tcs);
            self.AddTimer(timerId, ref timer);

            //打断该计时任务的回调
            void CancelAction()
            {
                if (self.Remove(timerId))
                {
                    tcs.SetResult();
                }
            }

            try
            {
                //如果需要，则绑定移除计时任务的回调到打断令牌上
                //外部想打断该任务，只需要调用cancellationToken.Cancle即可
                cancellationToken?.Add(CancelAction);
                //如果一切正常，该异步任务则会等待tcs任务完成，什么时候完成呢？等计时器时间到了，调到Run里面，针对TimerClass.OnceWaitTimer类型的任务，会调用到tcs.SetResult
                //tcs任务结束，同时该异步任务也结束，此处的await和外部的await都会完成。
                await tcs;
            }
            finally
            {
                //将绑定的回调移除，避免出现意料外的错误
                cancellationToken?.Remove(CancelAction);
            }
        }

        
        /// <summary>
        /// 等待一帧
        /// </summary>
        /// <param name="self"></param>
        /// <param name="cancellationToken"></param>
        public static async ETTask WaitFrameAsync(this TimerComponent self, ETCancellationToken cancellationToken = null)
        {
            await self.WaitAsync(1, cancellationToken);
        }

        /// <summary>
        /// 等待一段时间
        /// 和WaitTillAsync很像
        /// </summary>
        /// <param name="self"></param>
        /// <param name="time">等待的时间</param>
        /// <param name="cancellationToken"></param>
        public static async ETTask WaitAsync(this TimerComponent self, long time, ETCancellationToken cancellationToken = null)
        {
            if (time == 0)
            {
                return;
            }

            long timeNow = self.GetNow();

            ETTask tcs = ETTask.Create(true);
            long timerId = self.GetId();
            TimerAction timer = new (TimerClass.OnceWaitTimer, timeNow, time, 0, tcs);
            self.AddTimer(timerId, ref timer);

            void CancelAction()
            {
                if (self.Remove(timerId))
                {
                    tcs.SetResult();
                }
            }

            try
            {
                cancellationToken?.Add(CancelAction);
                await tcs;
            }
            finally
            {
                cancellationToken?.Remove(CancelAction);
            }
        }

        // 用这个优点是可以热更，缺点是回调式的写法，逻辑不连贯。WaitTillAsync不能热更，优点是逻辑连贯。
        // wait时间短并且逻辑需要连贯的建议WaitTillAsync
        // wait时间长不需要逻辑连贯的建议用NewOnceTimer
        public static long NewOnceTimer(this TimerComponent self, long tillTime, int type, object args)
        {
            long timeNow = self.GetNow();
            if (tillTime < timeNow)
            {
                Log.Error($"new once time too small: {tillTime}");
            }
            long timerId = self.GetId();
            TimerAction timer = new (TimerClass.OnceTimer, timeNow, tillTime - timeNow, type, args);
            self.AddTimer(timerId, ref timer);
            return timerId;
        }

        public static long NewFrameTimer(this TimerComponent self, int type, object args)
        {
#if DOTNET
            return self.NewRepeatedTimerInner(100, type, args);
#else
            return self.NewRepeatedTimerInner(0, type, args);
#endif
        }

        /// <summary>
        /// 创建一个RepeatedTimer
        /// </summary>
        private static long NewRepeatedTimerInner(this TimerComponent self, long time, int type, object args)
        {
#if DOTNET
            if (time < 100)
            {
                throw new Exception($"repeated timer < 100, timerType: time: {time}");
            }
#endif
            
            long timeNow = self.GetNow();
            long timerId = self.GetId();
            TimerAction timer = new (TimerClass.RepeatedTimer, timeNow, time, type, args);

            // 每帧执行的不用加到timerId中，防止遍历
            self.AddTimer(timerId, ref timer);
            return timerId;
        }

        public static long NewRepeatedTimer(this TimerComponent self, long time, int type, object args)
        {
            if (time < 100)
            {
                Log.Error($"time too small: {time}");
                return 0;
            }

            return self.NewRepeatedTimerInner(time, type, args);
        }
    }

    [ComponentOf(typeof(Scene))]
    public class TimerComponent: Entity, IAwake, IUpdate
    {
        /// <summary>
        /// key: time, value: timer id
        /// </summary>
        public readonly NativeCollection.MultiMap<long, long> timeId = new(1000);

        public readonly Queue<long> timeOutTime = new();

        public readonly Queue<long> timeOutTimerIds = new();

        public readonly Dictionary<long, TimerAction> timerActions = new();

        public long idGenerator;

        // 记录最小时间，不用每次都去MultiMap取第一个值
        public long minTime = long.MaxValue;
    }
}