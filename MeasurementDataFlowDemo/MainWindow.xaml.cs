using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using OxyPlot;

namespace MeasurementDataFlowDemo
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public IList<DataPoint> Datas { get; set; } = new List<DataPoint>();

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

        /// <summary>
        /// 每个X的索引
        /// </summary>
        private int _xIndex;


        /// <summary>
        /// 测量结束标记
        /// </summary>
        private bool _stop;

        /// <summary>
        /// 文件流
        /// </summary>
        private FileStream _fileStream;

        /// <summary>
        /// 写入二进制
        /// </summary>
        private BinaryWriter _binaryWriter;

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            
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
            this.LinkBlocks();
        }

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
            while (!this._stop)
            {
                queue.Enqueue(random.NextDouble() * 10);
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
        }
        /// <summary>
        /// 开始测量事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StartMeasureButton_OnClick(object sender, RoutedEventArgs e)
        {
            this.Datas.Clear();
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
                        this._queue1.TryDequeue(out var result);
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
        }
        /// <summary>
        /// 停止测量事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StopMeasureButton_OnClick(object sender, RoutedEventArgs e)
        {
            this._stop = true;
            this._binaryWriter.Flush();
            this._binaryWriter.Close();
            this._fileStream.Close();
        }
    }
}