# MeasurementDataFlowDemo
#像Labview一样，使用C#构建测量数据流式处理框架

## 1. C# DataFlow介绍

介绍部分参考博客：[TPL DataFlow初探（一）](https://www.cnblogs.com/haoxinyue/archive/2013/03/01/2938959.html)
侵权请联系删除

官方解释：

> TPL（任务并行库） 数据流库向具有高吞吐量和低滞后时间的占用大量 CPU 和 I/O 操作的应用程序的并行化和消息传递提供了基础。 它还能显式控制缓存数据的方式以及在系统中移动的方式。传统编程模型通常需要使用回调和同步对象（例如锁）来协调任务和访问共享数据。在数据流模型下，您可以声明当数据可用时的处理方式，以及数据之间的所有依赖项。 由于运行时管理数据之间的依赖项，因此通常可以避免这种要求来同步访问共享数据。 此外，因为运行时计划基于数据的异步到达，所以数据流可以通过有效管理基础线程提高响应能力和吞吐量。

借助于异步消息传递与管道，它可以提供比线程池更好的控制，也比手工线程方式具备更好的性能。我们常常可以消息传递，生产-消费模式或Actor-Agent模式中使用。在TDF是构建于Task Parallel Library (TPL)之上的，它是我们开发高性能，高并发的应用程序的又一利器。

TDP的主要作用就是Buffering Data和Processing Data，在TDF中，有两个非常重要的接口，ISourceBlock<T> 和ITargetBlock<T>接口。继承于ISourceBlock<T>的对象时作为提供数据的数据源对象-生产者，而继承于ITargetBlock<T>接口类主要是扮演目标对象-消费者。在这个类库中，System.Threading.Tasks.Dataflow名称空间下，提供了很多以Block名字结尾的类，ActionBlock，BufferBlock，TransformBlock，BroadcastBlock等9个Block，我们在开发中通常使用单个或多个Block组合的方式来实现一些功能。

## 1.1 9个典型Block的使用

### 1.1.1 BufferBlock

BufferBlock是TDF中最基础的Block。BufferBlock提供了一个有界限或没有界限的Buffer，该Buffer中存储T。该Block很像BlockingCollection<T>。可以用过Post往里面添加数据，也可以通过Receive方法阻塞或异步的的获取数据，数据处理的顺序是FIFO的。它也可以通过Link向其他Block输出数据。

![](https://file.spartajet.com/20180821171329.png)

```
private static BufferBlock<int> m_buffer = new BufferBlock<int>();

// Producer
private static void Producer()
{
    while(true)
    {
        int item = Produce();
        m_buffer.Post(item);
    }
}

// Consumer
private static void Consumer()
{
    while(true)
    {
        int item = m_buffer.Receive();
        Process(item);
    }
}

// Main
public static void Main()
{
    var p = Task.Factory.StartNew(Producer);
    var c = Task.Factory.StartNew(Consumer);
    Task.WaitAll(p,c);
}
```
### 1.1.2 ActionBlock

ActionBlock实现ITargetBlock，说明它是消费数据的，也就是对输入的一些数据进行处理。它在构造函数中，允许输入一个委托，来对每一个进来的数据进行一些操作。如果使用Action(T)委托，那说明每一个数据的处理完成需要等待这个委托方法结束，如果使用了Func<TInput, Task>)来构造的话，那么数据的结束将不是委托的返回，而是Task的结束。默认情况下，ActionBlock会FIFO的处理每一个数据，而且一次只能处理一个数据，一个处理完了再处理第二个，但也可以通过配置来并行的执行多个数据。

![](https://file.spartajet.com/20180821171440.png)

```
public ActionBlock<int> abSync = new ActionBlock<int>((i) =>
            {
                Thread.Sleep(1000);
                Console.WriteLine(i + " ThreadId:" + Thread.CurrentThread.ManagedThreadId + " Execute Time:" + DateTime.Now);
            }
        );

        public void TestSync()
        {
            for (int i = 0; i < 10; i++)
            {
                abSync.Post(i);
            }

            Console.WriteLine("Post finished");
        }
```

### 1.1.3 TransformBlock

TransformBlock是TDF提供的另一种Block，顾名思义它常常在数据流中充当数据转换处理的功能。在TransformBlock内部维护了2个Queue，一个InputQueue，一个OutputQueue。InputQueue存储输入的数据，而通过Transform处理以后的数据则放在OutputQueue，OutputQueue就好像是一个BufferBlock。最终我们可以通过Receive方法来阻塞的一个一个获取OutputQueue中的数据。TransformBlock的Completion.Wait()方法只有在OutputQueue中的数据为0的时候才会返回。

![](https://file.spartajet.com/20180821171656.png)

举个例子，我们有一组网址的URL，我们需要对每个URL下载它的HTML数据并存储。那我们通过如下的代码来完成：

```
public TransformBlock<string, string> tbUrl = new TransformBlock<string, string>((url) =>
        {
            WebClient webClient = new WebClient();
            return webClient.DownloadString(new Uri(url));
        }

        public void TestDownloadHTML()
        {
            tbUrl.Post("www.baidu.com");
            tbUrl.Post("www.sina.com.cn");

            string baiduHTML = tbUrl.Receive();
            string sinaHTML = tbUrl.Receive();
        }
```

**其他的Block请参考上述博客**

## 2. 实例

### 2.1 测试需求

我们需要采集三个通道的数据$x$,$y$,$z$. 然后使用$x$,$y$,$z$组合经过如下公式计算得到一个结果$m$

$$m=x*y+z $$

然后对$m$数列做中值滤波，每5个数值求中间值，生成一个最终的值。

得到最终值之后，在界面中显示波形，存二进制文件，通过网络发送到数据服务器。

其流式处理图如下：
![](https://file.spartajet.com/20180821182858.png)

### 2.2 业务分析

按照C# Dataflow的思想，流式处理的各个节点可以使用的Block如下图：

![](https://file.spartajet.com/20180821183749.png)

细心的人可以看到，最后的业务处理前面加上了一个`BroadCastBlock`，是为了同时给三个业务分发消息。

### 2.3 业务实现

#### 2.3.1 架构设计

本博客采用WPF窗体框架，界面如下

![](https://file.spartajet.com/20180821191454.png)


#### 2.3.2 代码实现
由于手上确实没有合适的板卡做测试，我就用三个Task模拟数据生成，然后放入三个`ConcurrentQueue`.

代码如下：

```
/// <summary>
/// 通道1队列
/// </summary>
private readonly ConcurrentQueue<double> _queue1 = new ConcurrentQueue<double>();

/// <summary>
/// 通道2队列
/// </summary>
private readonly ConcurrentQueue<double> _queue2 = new ConcurrentQueue<double>();

/// <summary>
/// 通道3队列
/// </summary>
private readonly ConcurrentQueue<double> _queue3 = new ConcurrentQueue<double>();


/// <summary>
/// 生成通道1数据按钮事件
/// </summary>
/// <param name="sender"></param>
/// <param name="e"></param>
private void GenerateChannel1Button_OnClick(object sender, RoutedEventArgs e)
{
    Task.Factory.StartNew(() => GenerateData(this._queue1));
}

/// <summary>
/// 生成通道2数据按钮事件
/// </summary>
/// <param name="sender"></param>
/// <param name="e"></param>
private void GenerateChannel2Button_OnClick(object sender, RoutedEventArgs e)
{
    Task.Factory.StartNew(() => GenerateData(this._queue2));
}

/// <summary>
/// 生成通道3数据按钮事件
/// </summary>
/// <param name="sender"></param>
/// <param name="e"></param>
private void GenerateChannel3Button_OnClick(object sender, RoutedEventArgs e)
{
    Task.Factory.StartNew(() => GenerateData(this._queue3));
}

/// <summary>
/// 生成数据
/// </summary>
/// <param name="queue"></param>
/// <returns></returns>
private async Task GenerateData(ConcurrentQueue<double> queue)
{
    var random = new Random();
    while (this._stop)
    {
        queue.Enqueue(random.NextDouble() * 10);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
    }
}

```

初始化各种Block

```
/// <summary>
/// 通道1BufferBlock
/// </summary>
private BufferBlock<double> _bufferBlock1 = new BufferBlock<double>();

/// <summary>
/// 通道2BufferBlock
/// </summary>
private BufferBlock<double> _bufferBlock2 = new BufferBlock<double>();

/// <summary>
/// 通道3BufferBlock
/// </summary>
private BufferBlock<double> _bufferBlock3 = new BufferBlock<double>();

/// <summary>
/// 拼接3个通道JoinBlock
/// </summary>
private JoinBlock<double, double,double> _joinBlock = new JoinBlock<double, double, double>();

/// <summary>
/// 计算M的TransformBlock
/// </summary>
private TransformBlock<Tuple<double,double,double>, double> _calculateMTransformBlock =
    new TransformBlock<Tuple<double, double, double>, double>(t => t.Item1 * t.Item2 + t.Item3);

/// <summary>
/// 每5个m组成一组BatchBlock
/// </summary>
private BatchBlock<double> _mBatchBlock = new BatchBlock<double>(5);

/// <summary>
/// m的中值滤波TransformBlock
/// </summary>
private TransformBlock<double[], double> _mMiddleFilterTransformBlock = new TransformBlock<double[], double>(
    t =>
    {
        Array.Sort(t);
        return t[2];
    });

/// <summary>
/// 广播mBroadcastBlock
/// </summary>
private BroadcastBlock<double> _broadcastBlock = new BroadcastBlock<double>(t => t);

/// <summary>
/// 界面显示ActionBlock
/// </summary>
private ActionBlock<double> _showPlotActionBlock;

/// <summary>
/// 写入文件ActionBlock
/// </summary>
private ActionBlock<double> _writeFileActionBlock;
/// <summary>
/// 网络上传ActionBlock
/// </summary>
private ActionBlock<double> _netUpActionBlock;

```

由于`Lambda` 需要访问外部变量，则需要在`laod`事件中初始化：

```
//UI显示ActionBlock
this._showPlotActionBlock = new ActionBlock<double>(t =>
{
    if (this.Datas.Count >= 10000)
    {
        this.Datas.RemoveAt(0);
    }

    this.Datas.Add(new DataPoint(_xIndex++, t));
    Application.Current.Dispatcher.Invoke(() => { Plot?.InvalidatePlot(); });
}, new ExecutionDataflowBlockOptions()
{
    TaskScheduler = TaskScheduler.FromCurrentSynchronizationContext()
});
//写入文件
this._writeFileActionBlock = new ActionBlock<double>(t =>
{
    this._binaryWriter.Write(t);
});
//上传数据，暂时不实现
this._netUpActionBlock = new ActionBlock<double>(t => Console.WriteLine($@"Net upload value: {t}"));

```
链接这些Block

```
/// <summary>
/// 链接Blocks
/// </summary>
private void LinkBlocks()
{
    this._bufferBlock1.LinkTo(this._joinBlock.Target1);
    this._bufferBlock2.LinkTo(this._joinBlock.Target2);
    this._bufferBlock3.LinkTo(this._joinBlock.Target3);
    this._joinBlock.LinkTo(this._calculateMTransformBlock);
    this._calculateMTransformBlock.LinkTo(this._mBatchBlock);
    this._mBatchBlock.LinkTo(this._mMiddleFilterTransformBlock);
    this._mMiddleFilterTransformBlock.LinkTo(this._broadcastBlock);
    this._broadcastBlock.LinkTo(this._showPlotActionBlock);
    this._broadcastBlock.LinkTo(this._writeFileActionBlock);
    this._broadcastBlock.LinkTo(this._netUpActionBlock);
}
```

开始测量按钮事件：

```
this._stop = false;
this._fileStream = new FileStream($"{DateTime.Now:yyyy_MM_dd_HH_mm_ss}.dat", FileMode.OpenOrCreate,
    FileAccess.Write);
this._binaryWriter = new BinaryWriter(this._fileStream);
Task.Factory.StartNew(async () =>
{
    while (!this._stop)
    {
        if (this._queue1.Count > 0)
        {
            double result;
            this._queue1.TryDequeue(out result);
            this._bufferBlock1.Post(result);
        }
        else
        {
            await Task.Delay(TimeSpan.FromMilliseconds(30));
        }

    }
});
Task.Factory.StartNew(async () =>
{
    while (!this._stop)
    {
        if (this._queue2.Count > 0)
        {
            double result;
            this._queue2.TryDequeue(out result);
            this._bufferBlock2.Post(result);
        }
        else
        {
            await Task.Delay(TimeSpan.FromMilliseconds(30));
        }

    }
});
Task.Factory.StartNew(async () =>
{
    while (!this._stop)
    {
        if (this._queue3.Count > 0)
        {
            double result;
            this._queue3.TryDequeue(out result);
            this._bufferBlock3.Post(result);
        }
        else
        {
            await Task.Delay(TimeSpan.FromMilliseconds(30));
        }

    }
});

```

结束测量事件

```
this._stop = true;
this._binaryWriter.Flush();
this._binaryWriter.Close();
this._fileStream.Close();

```
最终的效果：
![](https://file.spartajet.com/2018-08-21_21-02-51.gif)

#### 2.3.3 效果体验：

1. 先点击三个生个数据按钮，然后开始测量，可以看到数据马上就会显示，同时会保存，也会上传数据。停止测量后可以看到数据保存到了文件中。
2. 先点击生成通道1和2生成数据，不点击通道3，然后点击开始测量，可以看到没有反应，再点击通道3，就有数据了，根据我们的测量逻辑，这个是对的。


本文源码已发布到github，地址：https://github.com/spartajet/MeasurementDataFlowDemo