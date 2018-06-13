using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using AForge.Video;
using AForge.Video.DirectShow;
using Microsoft.Cognitive.CustomVision.Prediction;
using Microsoft.Cognitive.CustomVision.Training;
using Microsoft.Cognitive.CustomVision.Training.Models;

namespace CustomVisionTestApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {

        #region Public properties

        public ObservableCollection<FilterInfo> VideoDevices { get; set; }

        public FilterInfo CurrentDevice
        {
            get { return _currentDevice; }
            set { _currentDevice = value; this.OnPropertyChanged("CurrentDevice"); }
        }
        private FilterInfo _currentDevice;

        #endregion


        #region Private fields
        private static List<string> validImages;
        private static List<string> invalidImages;
        private static MemoryStream testImage;
        private IVideoSource _videoSource;
        private bool snap;
        private string root = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        //Custom Vision Project
        Project project;
        //Our training API to use for Creating, Uploading, and Training
        TrainingApi trainingApi;
        //Create the tags we will use in the project
        Tag validTag;
        Tag invalidTag;
        #endregion

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            GetVideoDevices();
            this.Closing += MainWindow_Closing;
            //This is for determining if we are snapping a picture
            //setting to false to start so video plays
            snap = false;
            // Setting up training keys for use in the program.
            // Add your training key from the settings page of the portal
            string trainingKey = "7d5baab1eb8842418d88515c4c4ee416";

            // Create the Api, passing in the training key
            trainingApi = new TrainingApi() { ApiKey = trainingKey };
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopCamera();
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            StartCamera();
        }

        private void video_NewFrame(object sender, AForge.Video.NewFrameEventArgs eventArgs)
        {
            try
            {
                BitmapImage bi;
                using (var bitmap = (Bitmap)eventArgs.Frame.Clone())
                {
                    bi = bitmap.ToBitmapImage();
                    if (snap)
                    {
                        //settingn folder for pulling up image already saved (in assests folder)
                        
                        var imagePath = System.IO.Path.Combine(root, "../../Evaluated/");
                        string cameraPic = imagePath + "evaluated.jpg";
                        bitmap.Save(cameraPic);
                        snap = false;
                    }
                    
                }
                bi.Freeze(); // avoid cross thread operations and prevents leaks
                Dispatcher.BeginInvoke(new ThreadStart(delegate { videoPlayer.Source = bi; }));
            }
            catch (Exception exc)
            {
                MessageBox.Show("Error on _videoSource_NewFrame:\n" + exc.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StopCamera();
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
        }

        private void GetVideoDevices()
        {
            VideoDevices = new ObservableCollection<FilterInfo>();
            foreach (FilterInfo filterInfo in new FilterInfoCollection(FilterCategory.VideoInputDevice))
            {
                VideoDevices.Add(filterInfo);
            }
            if (VideoDevices.Any())
            {
                CurrentDevice = VideoDevices[0];
            }
            else
            {
                MessageBox.Show("No video sources found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartCamera()
        {
            if (CurrentDevice != null)
            {
                _videoSource = new VideoCaptureDevice(CurrentDevice.MonikerString);
                _videoSource.NewFrame += video_NewFrame;
                _videoSource.Start();
            }
        }

        private void StopCamera()
        {
            if (_videoSource != null && _videoSource.IsRunning)
            {
                _videoSource.SignalToStop();
                _videoSource.NewFrame -= new NewFrameEventHandler(video_NewFrame);
            }
        }

        #region INotifyPropertyChanged members

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null)
            {
                var e = new PropertyChangedEventArgs(propertyName);
                handler(this, e);
            }
        }

        #endregion

        private void btnEval_Click(object sender, RoutedEventArgs e)
        {
            snap = true;
            Log("I just snapped a picture.");
            EvaluateImages();
        }

        private async void EvaluateImages()
        {
            // Add your prediction key from the settings page of the portal
            // The prediction key is used in place of the training key when making predictions
            string predictionKey = "ab7cbbf3606a41308e3c66d73c4af3fa";

            // Create a prediction endpoint, passing in obtained prediction key
            PredictionEndpoint endpoint = new PredictionEndpoint() { ApiKey = predictionKey };
            // Get image to test
            var imagePath = System.IO.Path.Combine(root, "../../Evaluated/evaluated.jpg");
            testImage = new MemoryStream(File.ReadAllBytes(imagePath));

            // Make a prediction against the new project
            Log("Making a prediction:");
            //Hardcoding projectID. 
            //Guid projectID = new Guid("d8622071-5654-435d-a776-83944fc43129");
            var result = await endpoint.PredictImageAsync(project.Id, testImage);

            // Loop over each prediction and write out the results
            foreach (var c in result.Predictions)
            {
                Log($"\t{c.Tag}: {c.Probability:P1}");
            }

        }

        public void Log(string logMessage)
        {
            if (String.IsNullOrEmpty(logMessage) || logMessage == "\n")
            {
                _logTextBox.Text += "\n";
            }
            else
            {
                string timeStr = DateTime.Now.ToString("HH:mm:ss.ffffff");
                string messaage = "[" + timeStr + "]: " + logMessage + "\n";
                _logTextBox.Text += messaage;
            }
            _logTextBox.ScrollToEnd();
        }

        private void btnCreate_Click(object sender, RoutedEventArgs e)
        {

            // Create a new project
            Log("Creating new project:");
            project = trainingApi.CreateProject("planogram3");
            Log($"Project {project.Name} - {project.Id} created ");

            // Make two tags in the new project
            validTag = trainingApi.CreateTag(project.Id, "valid");
            invalidTag = trainingApi.CreateTag(project.Id, "invalid");

            Log($"Tags {validTag.Name} - {invalidTag.Name} created ");

            Log($"Creation Completed");

        }

        private void btnUpload_Click(object sender, RoutedEventArgs e)
        {

            Log("Loading Images from Disk");
            LoadImagesFromDisk();
            Log("Loading Images Complete");


            Log("Uploading images to service");
            // Images can be uploaded one at a time
            foreach (var image in validImages)
            {
                using (var stream = new MemoryStream(File.ReadAllBytes(image)))
                {
                    trainingApi.CreateImagesFromData(project.Id, stream, new List<string>() { validTag.Id.ToString() });
                }
            }

            // Or uploaded in a single batch 
            var imageFiles = invalidImages.Select(img => new ImageFileCreateEntry(Path.GetFileName(img), File.ReadAllBytes(img))).ToList();
            trainingApi.CreateImagesFromFiles(project.Id, new ImageFileCreateBatch(imageFiles, new List<Guid>() { invalidTag.Id }));
            Log("Images Uploaded");
        }



        private async void btnTrain_Click(object sender, RoutedEventArgs e)
        {
            //btnTrain.IsEnabled = false;
            //var someTask = Task.Factory.StartNew(() => Train());
            //await someTask;

            //btnTrain.IsEnabled =  true;


            // Now there are images with tags start training the project
            Log("Training");
            var iteration = await trainingApi.TrainProjectAsync(project.Id);

            // The returned iteration will be in progress, and can be queried periodically to see when it has completed
            while (iteration.Status == "Training")
            {
                Thread.Sleep(1000);

                // Re-query the iteration to get it's updated status
                iteration = trainingApi.GetIteration(project.Id, iteration.Id);
            }

            // The iteration is now trained. Make it the default project endpoint
            iteration.IsDefault = true;
            trainingApi.UpdateIteration(project.Id, iteration.Id, iteration);
            Log("Done Training!");

        }
        //private async Task Train()
        //{

            
        //}

        private static void LoadImagesFromDisk()
        {
            // this loads the images to be uploaded from disk into memory
            validImages = Directory.GetFiles(@"..\..\Images\valid").ToList();
            invalidImages = Directory.GetFiles(@"..\..\Images\invalid").ToList();
           
        }
    }
}
