using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Kinect;
using System.Windows.Threading;
using System.Net.Http;

namespace KinectSpheroController
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        KinectSensor _sensor;
        bool closing = false;
        const int skeletonCount = 6;
        Skeleton[] allSkeletons = new Skeleton[skeletonCount];
        DispatcherTimer _zeroPointTimer;
        DispatcherTimer _commandReadyTimer;
        bool commandReady = true;
        bool connected = false;
        bool commandInProgress = false;
        bool calibrationMode = false;
        HttpClient spheroClient = new HttpClient();

        const string JAVA_SERVER_URI = "http://localhost:9000/";

        int baseX_p1;
        int baseY_p1;
        int baseZ_p1;

        int player1;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (KinectSensor.KinectSensors.Count > 0)
            {
                _sensor = KinectSensor.KinectSensors[0];

                if (_sensor.Status == KinectStatus.Connected)
                {
                    _sensor.ColorStream.Enable();
                    _sensor.DepthStream.Enable();
                    var parameters = new TransformSmoothParameters
                    {
                        Smoothing = 0.3f,
                        Correction = 0.0f,
                        Prediction = 0.0f,
                        JitterRadius = 1.0f,
                        MaxDeviationRadius = 0.5f
                    };
                    _sensor.SkeletonStream.Enable(parameters);

                    _sensor.AllFramesReady += _sensor_AllFramesReady;

                    _zeroPointTimer = new DispatcherTimer();
                    _zeroPointTimer.Tick += _zeroPointTimer_Tick;
                    _zeroPointTimer.Interval = new TimeSpan(0, 0, 1);

                    _commandReadyTimer = new DispatcherTimer();
                    _commandReadyTimer.Tick += _commandReadyTimer_Tick;
                    _commandReadyTimer.Interval = new TimeSpan(0, 0, 0, 0, 200);

                    spheroClient.DefaultRequestHeaders.Accept.Clear();
                    spheroClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                    try
                    {
                        _sensor.Start();
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
            }
        }

        private void _commandReadyTimer_Tick(object sender, EventArgs e)
        {
            commandReady = true;
            _commandReadyTimer.Stop();
        }

        private void _zeroPointTimer_Tick(object sender, EventArgs e)
        {
            _zeroPointTimer.Stop();
            connected = true;
        }

        private void _sensor_AllFramesReady(object sender, AllFramesReadyEventArgs e)
        {
            bool playerVisible = false;

            if (closing)
            {
                return;
            }

            using (ColorImageFrame colorFrame = e.OpenColorImageFrame())
            {
                if (colorFrame == null)
                {
                    return;
                }

                byte[] pixels = new byte[colorFrame.PixelDataLength];
                colorFrame.CopyPixelDataTo(pixels);

                int stride = colorFrame.Width * 4;
                image.Source = BitmapSource.Create(
                    colorFrame.Width, colorFrame.Height, 96, 96,
                    PixelFormats.Bgr32, null, pixels, stride);

                SkeletonFrame skeletonFrameData = e.OpenSkeletonFrame();

                if (skeletonFrameData == null)
                {
                    return;
                }

                skeletonFrameData.CopySkeletonDataTo(allSkeletons);

                int i = 0;
                foreach (Skeleton skeleton in this.allSkeletons.Where(s => s.TrackingState != SkeletonTrackingState.NotTracked))
                {
                    if (i == 0)
                    {
                        player1 = skeleton.TrackingId;
                        playerVisible = true;
                    }
                    GetCameraPoint(skeleton, e, skeleton.TrackingId);
                    i++;
                }
            }

            player1TitleLabel.Content = connected ? "Player 1" : "";

        }

        void GetCameraPoint(Skeleton first, AllFramesReadyEventArgs e, int playerId)
        {
            using (DepthImageFrame depth = e.OpenDepthImageFrame())
            {
                if (depth == null ||
                    _sensor == null)
                {
                    return;
                }

                //Map a joint location to a point on the depth map
                DepthImagePoint leftHand = _sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(first.Joints[JointType.HandLeft].Position, DepthImageFormat.Resolution640x480Fps30);
                DepthImagePoint rightHand = _sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(first.Joints[JointType.HandRight].Position, DepthImageFormat.Resolution640x480Fps30);
                DepthImagePoint rightHip = _sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(first.Joints[JointType.HipRight].Position, DepthImageFormat.Resolution640x480Fps30);

                //Height value is inverted. High value is low point
                if (rightHand.Y < rightHip.Y)
                {
                    _zeroPointTimer.Start();

                    if (connected)
                    {
                        lblLeft.Content = "--";
                        lblRight.Content = "--";

                        if (calibrationMode)
                        {
                            //exit calibration mode
                            //postActionToSphero("endCalibration");
                            SetPlayerCalibrationMode(false);
                        }

                        double deltaY = 0;
                        double deltaX = 0;

                        deltaY = rightHand.Depth - baseZ_p1;
                        deltaX = rightHand.X - baseX_p1;

                        double speed = Math.Abs(Math.Round((deltaX + deltaY) / 5));
                        double angle = Math.Round(Math.Atan2(deltaY, deltaX) * 180 / Math.PI + 90);

                        if (angle < 0)
                        {
                            angle = 360 - Math.Abs(angle);
                        }

                        lblX.Content = "Speed: " + speed;
                        lblY.Content = "Angle: " + angle;
                        lblLeft.Content = leftHand.Depth - baseZ_p1;

                        if (speed > 5)
                        {
                            if ((angle >= 45 && angle < 135) || (angle >= 225 && angle < 315))
                            {
                                speed = speed * 2;
                            }
                            //speed = 30;
                            //if (angle >= 315 || angle < 45)
                            //{
                            //    angle = 0;
                            //} else if (angle >= 45 && angle < 135)
                            //{
                            //    angle = 90;
                            //} else if (angle >= 135 && angle < 225)
                            //{
                            //    angle = 180;
                            //} else if (angle >= 225)
                            //{
                            //    angle = 270;
                            //}
                            try
                            {
                                PostJsonToSphero(speed, angle);
                            }
                            catch (Exception ex)
                            {
                                var ExceptionMessage = ex.Message;
                                throw ex;
                            }
                        }
                    } //else
                    //{
                    //    HttpResponseMessage response = await PostActionToSphero("connect");
                    //}
                    //else if (leftHand.Y < rightHip.Y)
                    //{
                    //    _zeroPointTimer.Stop();;

                    //    lblLeft.Content = leftHand.Depth - baseZ;
                    //    lblRight.Content = rightHand.Depth - baseZ;

                    //    //both hands up
                    //    if (Math.Abs(leftHand.Y - rightHand.Y) > 100)
                    //    {
                    //        if (PlayerInCalibrationMode(player))
                    //        {
                    //            lblLeft.Content = (leftHand.Depth - baseZ) * .2;
                    //            lblRight.Content = (rightHand.Depth - baseZ) * .2;

                    //            if (leftHand.Y > rightHand.Y)
                    //            {
                    //                //left hand closer
                    //            }
                    //            else
                    //            {
                    //                //right hand closer

                    //            }
                    //        }
                    //        else
                    //        {
                    //            //start Calibration Mode
                    //            PostActionToSphero("calibrate", player);
                    //            SetPlayerCalibrationMode(player, true);
                    //        }
                    //    }
                    //}
                    else
                    {
                        baseX_p1 = rightHand.X;
                        baseY_p1 = rightHand.Y;
                        baseZ_p1 = rightHand.Depth;
                    }
                }
                else
                {
                    if (connected)
                    {
                        _zeroPointTimer.Stop();
                        connected = false;
                    }
                    if (calibrationMode)
                    {
                        //exit calibration mode
                        Task<HttpResponseMessage> response = PostActionToSphero("endCalibration");
                        SetPlayerCalibrationMode(false);
                    }

                }

                //Map a depth point to a point on the color image
                ColorImagePoint redColorPoint = _sensor.CoordinateMapper.MapDepthPointToColorPoint(
                    DepthImageFormat.Resolution640x480Fps30, rightHand,
                    ColorImageFormat.RgbResolution640x480Fps30);
                CameraPosition(redElipses, redColorPoint);
            }
        }

        private async Task<HttpResponseMessage> PostActionToSphero(string action)
        {
            HttpResponseMessage response = null;
            if (!commandInProgress)
            {
                commandInProgress = true;

                response = await spheroClient.PostAsync(
                    JAVA_SERVER_URI + action,
                    new StringContent("", Encoding.UTF8, "application/json")
                    );

                commandInProgress = false;
            }

            return response;
        }

        private async void PostJsonToSphero(double speed, double angle)
        {
            if (commandReady)
            {
                commandReady = false;

                string json = @"{""direction"": " + angle.ToString() + @",""speed"": " + speed.ToString() + "}";
                _commandReadyTimer.Start();

                try
                {
                    HttpResponseMessage wcfResponse = await spheroClient.PostAsync(JAVA_SERVER_URI, new StringContent(json, Encoding.UTF8, "application/json"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        private void SetPlayerCalibrationMode(bool value)
        {
            calibrationMode = value;
        }

        private void ResetPlayerCommandReadyTimer()
        {
            commandReady = false;
            _commandReadyTimer.Start();
        }

        //Skeleton GetFirstSkeleton(AllFramesReadyEventArgs e)
        //{
        //    using (SkeletonFrame skeletonFrameData = e.OpenSkeletonFrame())
        //    {
        //        if (skeletonFrameData == null)
        //        {
        //            return null;
        //        }

        //        skeletonFrameData.CopySkeletonDataTo(allSkeletons);

        //        //get the first tracked skeleton
        //        Skeleton first = (from s in allSkeletons
        //                          where s.TrackingState == SkeletonTrackingState.Tracked
        //                          select s).FirstOrDefault();

        //        Skeleton first = (from s in allSkeletons
        //                          where s.TrackingState == SkeletonTrackingState.Tracked
        //                          select s).;

        //        return first;
        //    }
        //}

        private void CameraPosition(FrameworkElement element, ColorImagePoint point)
        {
            //Divide by 2 for width and height so point is right in the middle 
            // instead of in top/left corner
            Canvas.SetLeft(element, (point.X) + 275);
            Canvas.SetTop(element, (point.Y - element.Height / 2) + 10);
        }

        void StopKinect(KinectSensor sensor)
        {
            if (sensor != null)
            {
                sensor.Stop();
                //sensor.AudioSource.Stop();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {

        }
    }
}
