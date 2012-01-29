using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using System.Reflection;
using System.Xml;
using SmugWrapperLibrary;
using System.Diagnostics;

namespace Keg2Smug
{
    class Keg2Smug
    {
        private string[] args;
        private const string kegUserAgent = "Mozilla/4.0 (compatible; MSIE 7.0; Windows NT 5.2; WOW64; .NET CLR 2.0.50727; .NET CLR 3.0.04506.30; .NET CLR 3.0.04506.648; InfoPath.2; .NET CLR 3.5.21022)";
        //private const string kegUserAgent = "Test/1.0";
        private string smugUserAgent = null;
        private const string API_KEY = "v94Pnv8Lzf9Zc0jLLql2oUQecM74z4tL";
        private bool albumDesc = true;
        private bool photoCaptions = true;
        private string temporaryLocation = null;
        private List<KodakAlbum> kodakAlbums = null;
        private bool allAlbums = true;
        private SmugWrapper smClient = null;
        private SmugLogin smLogin;
        private bool testForKodakPremierMember = false;
        private Regex imageIdRegex;
        private CookieCollection kodakLoginCookies = null;

        public Keg2Smug(string[] args)
        {
            this.args = args;

            AssemblyName assemblyName = Assembly.GetExecutingAssembly().GetName();
            Version version = assemblyName.Version;
            string currentVersion = string.Format("{0}.{1}", version.Major, version.Minor);
            smugUserAgent = string.Format("Keg2Smug/{0}", currentVersion);

            imageIdRegex = new Regex("id=\"([0-9]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            ServicePointManager.Expect100Continue = false;
        }

        public void Run()
        {
            if (!ParseParameters())
            {
                return;
            }

            GetTemporaryLocation();

            GetKodakLogin();

            GetSmugmugLogin();

            GetKodakAlbums();

            SkipCompletedAlbums();

            ChooseKodakAlbums();

            FetchKodakAlbums();

            UploadAlbums();
        }

        /// <summary>
        /// Parse program parameters
        /// </summary>
        private bool ParseParameters()
        {
            foreach (string arg in args)
            {
                switch (arg)
                {
                    case "-nd":
                        this.albumDesc = false;
                        break;

                    case "-nc":
                        this.photoCaptions = false;
                        break;

                    default:
                        ShowUsage();
                        return false;
                }
            }

            Console.WriteLine("Migrate album descriptions: {0}", this.albumDesc);
            Console.WriteLine("Migrate photo captions: {0}", this.photoCaptions);
            Console.WriteLine("Run Keg2Smug.exe /? to get help on these options");
            Console.WriteLine();

            return true;
        }

        /// <summary>
        /// Get the location where to store temporary intermediate files and recovery data in case of interruption
        /// </summary>
        private void GetTemporaryLocation()
        {
            string currentDir = Environment.CurrentDirectory;
            bool done = false;

            while (!done)
            {
                Console.WriteLine();
                Console.WriteLine("Type the name of the temporary directory where Keg2Smug will store intermediate files and crash recovery data");
                Console.WriteLine("Or just hit enter to accept the default: {0}\\Keg2SmugData", currentDir);
                string input = null;
                do
                {
                    Console.Write(">");
                    input = Console.ReadLine();
                } while (input == null);

                if (input.Length == 0)
                {
                    input = string.Format("{0}\\Keg2SmugData", currentDir);
                }

                if (!Directory.Exists(input))
                {
                    Console.Write("Directory does not exist, create? (y/n)");
                    if (Console.ReadKey().KeyChar == 'y')
                    {
                        Directory.CreateDirectory(input);
                        done = true;
                        temporaryLocation = input;
                    }
                }
                else
                {
                    done = true;
                    temporaryLocation = input;
                }
            }
        }

        /// <summary>
        /// Log in to the Kodak photo gallery
        /// </summary>
        private void GetKodakLogin()
        {
            Console.WriteLine();

            string kodakUserName = ReadInputString("Kodak Easyshare Gallery username> ", false);
            string kodakPassword = ReadInputString("Kodak Easyshare Gallery password> ", false);

            // Fetch http://www.kodakgallery.com/Welcome.jsp and get a uv key
            string welcomeResponse;
            WebHeaderCollection outgoingHeaders = new WebHeaderCollection();
            WebHeaderCollection incomingHeaders = null;
            CookieCollection incomingCookies = null;
            if (HttpGet("http://www.kodakgallery.com/Welcome.jsp", outgoingHeaders, null, kegUserAgent, out incomingHeaders, out incomingCookies, out welcomeResponse) != HttpStatusCode.OK)
            {
                Console.WriteLine();
                Console.WriteLine("Unable to log in to Kodak Easyshare Gallery");
                Environment.Exit(10);
            }

            //string tempUVKey = ParseUVKey(welcomeResponse, "<a\\s*href=\"MyGallery\\.jsp\\?UV=(.+)\">");
            Cookie tempLoginCookie = ParseUVKey2(incomingCookies);

            string loginString = string.Format("signin=true&email={0}&password={1}", kodakUserName, kodakPassword);
            string loginUri = "https://secure.kodakgallery.com/Welcome.jsp?Upost_signin=Welcome.jsp";
            string referrerString = string.Format("http://www.kodakgallery.com/Welcome.jsp");

            outgoingHeaders = new WebHeaderCollection();

            string responseString = null;
            WebHeaderCollection incomingHeaders2 = null;
            CookieCollection outgoingCookies = new CookieCollection();
            outgoingCookies.Add(tempLoginCookie);
            CookieCollection incomingCookies2 = null;
            HttpStatusCode statusCode = HttpPost(loginUri,outgoingHeaders, outgoingCookies, referrerString, kegUserAgent, loginString, out incomingHeaders2, out incomingCookies2, out responseString);
            if (statusCode != HttpStatusCode.OK)
            {
                Console.WriteLine();
                Console.WriteLine("Unable to log in to Kodak Easyshare Gallery - statusCode {0}", statusCode);
                Environment.Exit(12);
            }

            //uvkey = ParseUVKey(responseString, "<a\\s*href=\"http://www.kodakgallery.com/MyGallery\\.jsp\\?[&]*UV=([0-9]+_[0-9]+)\"");
            this.kodakLoginCookies = new CookieCollection();
            Cookie loginCookie = ParseUVKey2(incomingCookies2);
            this.kodakLoginCookies.Add(loginCookie);

            Console.WriteLine("Succesfully logged in to Kodak Easyshare Gallery");
        }

        private string ParseUVKey(string input, string regexString)
        {
            Regex uvRegex = new Regex(regexString, RegexOptions.Compiled | RegexOptions.IgnoreCase);

            Match match = uvRegex.Match(input);

            if (match.Success)
            {
                return match.Groups[1].Captures[0].Value;
            }
            else
            {
                Console.WriteLine("Unable to parse Kodak Easyshare Gallery login response!");
                Environment.Exit(11);
                return null;
            }
        }

        private Cookie ParseUVKey2(CookieCollection cookies)
        {
            foreach (Cookie cookie in cookies)
            {
                if (cookie.Name == "V")
                {
                    // Clear the path so the cookie is sent to all pages in the domain
                    cookie.Path = "";
                    return cookie;
                }
            }
            return null;
        }

        private void GetKodakAlbums()
        {
            // Get the complete list of albums
            HttpStatusCode statusCode;
            string albumCountString;

            string albumListUri = "http://www.kodakgallery.com/AlbumMenu.jsp?view=1&page=1&sort_order=2&navfolderid=0&albumsperpage=64&displayallyears=1";

            if ((statusCode = HttpGet(albumListUri, kegUserAgent, out albumCountString)) != HttpStatusCode.OK)
            {
                Console.WriteLine("Unable to get list of albums from Kodak Easyshare Gallery - received response {0}", statusCode);
                Environment.Exit(2);
            }

            // Determine the count of albums
            Regex albumCountRegex = new Regex(@"All My Albums</a> \((?<AlbumCount>[0-9]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Match albumCountMatch = albumCountRegex.Match(albumCountString);

            if (!albumCountMatch.Success)
            {
                Console.WriteLine("Unable to determine number of Kodak Easyshare Gallery albums");
                Environment.Exit(13);
            }

            string albumCount = albumCountMatch.Groups[1].Captures[0].Value;

            int albumCountInt;
            if (!Int32.TryParse(albumCount, out albumCountInt))
            {
                Console.WriteLine("Failed to parse albumcount from {0}", albumCount);
                Environment.Exit(3);
            }

            Console.WriteLine("Expect to get {0} albums from Kodak Easyshare Gallery", albumCountInt);

            // Figure out how many pages of albums we have
            int numberOfPages = (albumCountInt / 64) + ((albumCountInt % 64) > 0 ? 1 : 0);

            // Fetch each page of albums, extract the albumIds
            kodakAlbums = new List<KodakAlbum>(albumCountInt);

            for (int i = 1; i <= numberOfPages; i++)
            {
                albumListUri = string.Format("http://www.kodakgallery.com/AlbumMenu.jsp?view=1&sort_order=2&navfolderid=0&albumsperpage=64&displayallyears=1&page={0}", i);

                string albumList;
                if ((statusCode = HttpGet(albumListUri, kegUserAgent, out albumList)) != HttpStatusCode.OK)
                {
                    Console.WriteLine("Failed to get album list for page {0} with statuscode {1}", i, statusCode);
                    Environment.Exit(4);
                }

                // Use a regex to get all the albumids on the page
                Regex albumIdAndNameRegex = new Regex("<p class=\"albumname\"><a.*collid=([0-9]+\\.[0-9]+)\\.[0-9]+.*>(.*)</a></p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                MatchCollection matches = albumIdAndNameRegex.Matches(albumList);
                Console.WriteLine("{0} albums found on page {1}", matches.Count, i);

                foreach (Match match in matches)
                {
                    GroupCollection groups = match.Groups;

                    string albumId = groups[1].Captures[0].Value;
                    string albumName = groups[2].Captures[0].Value;
                    albumName = HttpUtility.HtmlDecode(albumName);

                    KodakAlbum album = new KodakAlbum(albumId, albumName);

                    kodakAlbums.Add(album);
                }
            }

            if (kodakAlbums.Count != albumCountInt)
            {
                Console.WriteLine("AlbumCount is {0} but only got {1} albumss", albumCountInt, kodakAlbums.Count);
                Environment.Exit(5);
            }
            else
            {
                Console.WriteLine("Got all albums");
            }
        }

        /// <summary>
        /// Skip kodak albums which have already been uploaded to SmugMug
        /// </summary>
        private void SkipCompletedAlbums()
        {
            int skippedAlbums = 0;
            string statusFile = string.Format("{0}\\{1}", temporaryLocation, "keg2smug.status");
            if (File.Exists(statusFile))
            {
                using (FileStream fileStream = File.OpenRead(statusFile))
                {
                    using (StreamReader fileStreamReader = new StreamReader(fileStream))
                    {
                        // Read each line from the file
                        string currentAlbumId = fileStreamReader.ReadLine();
                        while (currentAlbumId != null)
                        {
                            KodakAlbum tempAlbum = new KodakAlbum(currentAlbumId, null);
                            if (kodakAlbums.Contains(tempAlbum))
                            {
                                kodakAlbums.Remove(tempAlbum);
                                skippedAlbums++;
                            }

                            currentAlbumId = fileStreamReader.ReadLine();
                        }
                        
                    }
                }
            }

            Console.WriteLine("Skipped {0} albums.", skippedAlbums);
            // If no albums left to upload, end
            if (kodakAlbums.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine("All albums have been uploaded to SmugMug");
                Console.WriteLine();
            }
        }


        private void ChooseKodakAlbums()
        {
            Console.WriteLine("Do you want to migrate all albums from Kodak Easyshare Gallery to SmugMug, or choose individual albums to migrate?");
            bool done = false;
            do
            {
                char input = ReadInputChar("Type a for all albums, i to pick individual albums> ");
                switch (input)
                {
                    case 'a':
                    case 'A':
                        allAlbums = true;
                        done = true;
                        break;

                    case 'i':
                    case 'I':
                        allAlbums = false;
                        done = true;
                        break;

                    default:
                        break;
                }
            } while (!done);

            if (!allAlbums)
            {
                List<KodakAlbum> albumsToRemove = new List<KodakAlbum>(kodakAlbums.Count);

                for (int i = 0; i < kodakAlbums.Count; i++)
                {
                    done = false;
                    do
                    {
                        char answer = ReadInputChar(string.Format("Include album ({0}/{1}) '{2}'? (y/n) >", i + 1, kodakAlbums.Count, kodakAlbums[i].albumName));
                        switch (answer)
                        {
                            case 'y':
                            case 'Y':
                                done = true;
                                break;

                            case 'n':
                            case 'N':
                                albumsToRemove.Add(kodakAlbums[i]);
                                done = true;
                                break;

                            default:
                                break;
                        }
                    } while (!done);
                }

                if (albumsToRemove.Count > 0)
                {
                    foreach (KodakAlbum album in albumsToRemove)
                    {
                        kodakAlbums.Remove(album);
                    }
                }
            }
        }

        /// <summary>
        /// Fetch all the kodak albums which are to be uploaded to SmugMug
        /// </summary>
        private void FetchKodakAlbums()
        {
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("Fetching album and photo information from Kodak Easyshare Gallery....this may take a while.");
            Console.WriteLine();

            HttpStatusCode statusCode;
            for (int i = 0; i < kodakAlbums.Count; i++)
            {
                KodakAlbum currentAlbum = kodakAlbums[i];

                // Check if the album metadata already exists on disk from a previous session - we dont need to get it again
                if (currentAlbum.ReadFromDisk(temporaryLocation))
                {
                    Console.WriteLine("Album \"{0}\" already exists on disk...will not fetch again.", currentAlbum.albumName);
                    continue;
                }
                else
                {
                    currentAlbum.CreateAlbumFolder(temporaryLocation);
                }

                Console.Write("Fetching metadata for album ({0}/{1}) - '{2}'......", i + 1, kodakAlbums.Count, currentAlbum.albumName);

                string albumUri = string.Format("http://www.kodakgallery.com/BrowsePhotos.jsp?collid={0}&page=1&sort_order=2", currentAlbum.albumId);

                string albumResponseString;
                if ((statusCode = HttpGet(albumUri, kegUserAgent, out albumResponseString)) != HttpStatusCode.OK)
                {
                    Console.WriteLine("Failed to get album '{0}' due to error {1}", currentAlbum.albumName, statusCode);
                    Environment.Exit(7);
                }

                // Get the album description, if requested
                if (albumDesc)
                {
                    Regex albumDescRegex = new Regex("<p id=\"collection-description\">(.*)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                    Match albumDescMatch = albumDescRegex.Match(albumResponseString);

                    string albumDescription = null;
                    if (albumDescMatch.Success)
                    {
                        albumDescription = albumDescMatch.Groups[1].Captures[0].Value;
                        currentAlbum.albumDescription = HttpUtility.HtmlDecode(albumDescription);
                    }
                }
                else
                {
                    currentAlbum.albumDescription = "";
                }

                // Get all the photo ids
                Regex photoIdRegex = new Regex("<a class=\"thumbnail\".*photoid=([0-9]+).*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                MatchCollection photoIdMatches = photoIdRegex.Matches(albumResponseString);

                currentAlbum.photos = new List<KodakPhoto>();

                foreach (Match match in photoIdMatches)
                {
                    GroupCollection groups = match.Groups;
                    string photoId = groups[1].Captures[0].Value;
                    KodakPhoto photo = new KodakPhoto(photoId);
                    currentAlbum.photos.Add(photo);
                }

                Console.WriteLine("done! Got metadata for {0} photos!", currentAlbum.photos.Count);

                int left = Console.CursorLeft;
                int top = Console.CursorTop;

                // Get photo captions, if requested
                if (photoCaptions)
                {
                    // Build a regex to extract the caption from each photo
                    Regex captionRegex = new Regex("<span id=\"photoCaption\">(.*)</span>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                    // Download caption for each photo
                    int captionIndex = 1;
                    foreach(KodakPhoto currentPhoto in currentAlbum.photos)
                    {
                        Console.SetCursorPosition(left, top);
                        Console.Write("Getting caption for photo ({0:D4}/{1})", captionIndex++, currentAlbum.photos.Count);
                        string photoUri = string.Format("http://www.kodakgallery.com/PhotoView.jsp?collid={0}&photoid={1}", currentAlbum.albumId, currentPhoto.photoId);

                        string photoResponseString = null;
                        if ((statusCode = HttpGet(photoUri, kegUserAgent, out photoResponseString)) != HttpStatusCode.OK)
                        {
                            Console.WriteLine("Failed to get photo {0} in album {1} with status {2}", currentPhoto.photoId, currentAlbum.albumId, statusCode);
                            Environment.Exit(8);
                        }

                        // Extract the caption from the response
                        Match captionMatch = captionRegex.Match(photoResponseString);
                        string caption = "";
                        if (captionMatch.Success)
                        {
                            caption = captionMatch.Groups[1].Captures[0].Value;
                            currentPhoto.photoCaption = HttpUtility.HtmlDecode(caption);
                        }
                    }
                    Console.WriteLine();
                }

                // Fetch the photos for this album
                int j = 0;
                foreach (KodakPhoto currentPhoto in currentAlbum.photos)
                {
                    Console.Write("Fetching photo {0:D4}/{1}...", j++, currentAlbum.photos.Count);
                    GetPhoto(currentAlbum, currentPhoto);
                }

                // Write the album info out to disk so we dont need to fetch it again in the future
                currentAlbum.WriteToDisk(temporaryLocation);
            }
        }


        private void GetSmugmugLogin()
        {
            string smugMugUsername = ReadInputString("SmugMug user name> ", false);
            string smugMugPassword = ReadInputString("SmugMug password> ", false);

            smClient = new SmugWrapper(API_KEY, smugUserAgent);
            try
            {
                this.smLogin = smClient.LoginWithPassword(smugMugUsername, smugMugPassword);
            }
            catch (Exception)
            {
                Console.WriteLine("Unable to log in to SmugMug!");
                Environment.Exit(6);
            }

            Console.WriteLine("Succesfully logged in to SmugMug");
        }


        private SmugAlbum GetDefaultSmugmugAlbum()
        {
            SmugAlbum album = new SmugAlbum();
            album.Public = false;
            album.SmugSearchable = false;
            album.CategoryID = 1;
            return album;
        }

        /// <summary>
        /// Upload all selected albums to SmugMug
        /// </summary>
        private void UploadAlbums()
        {
            Console.WriteLine();
            Console.WriteLine();
            
            for (int i = 0; i < kodakAlbums.Count; i++)
            {
                KodakAlbum currentAlbum = kodakAlbums[i];

                Console.WriteLine("Processing album ({0}/{1}) {2}", i + 1, kodakAlbums.Count, currentAlbum.albumName);

                // Create the album (if it doesnt already exist according to our recovery data)
                int smugAlbumId;
                if (!SmugMugAlbumExists(currentAlbum, out smugAlbumId))
                {
                    SmugAlbum album = GetDefaultSmugmugAlbum();
                    album.Title = currentAlbum.albumName;
                    album.Description = currentAlbum.albumDescription;

                    SmugAlbum createdAlbum = smClient.AlbumsCreate(smLogin.Session.id, album.Title, album.CategoryID, album);
                    smugAlbumId = createdAlbum.AlbumID;
                    UpdateSmugMugAlbumId(currentAlbum, smugAlbumId);
                    Console.WriteLine("Created album in SmugMug");
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("Album already exists. Skipping creation..", currentAlbum.albumName);
                }

                // Try to load the list of photos which have already been uploaded for this album
                List<string> existingPhotoIds = new List<string>(currentAlbum.photos.Count);
                LoadExistingPhotoList(currentAlbum, existingPhotoIds);

                for (int j = 0; j < currentAlbum.photos.Count; j++)
                {
                    if (existingPhotoIds.Contains(currentAlbum.photos[j].photoId))
                    {
                        Console.WriteLine();
                        Console.WriteLine("Skipping photo {0} in album {1} - it is marked as processed.", currentAlbum.photos[j].photoId, currentAlbum.albumName);
                        continue;
                    }

                    // Upload the photo to smugmug
                    Console.Write("Uploading photo ({0}/{1})....", j + 1, currentAlbum.photos.Count);
                    int smugPhotoId = UploadPhoto(currentAlbum, currentAlbum.photos[j], smugAlbumId);
                    // Once the photo is uploaded, change the position of the image so the album is sorted right
                    if (smugPhotoId != 0)
                    {
                        bool done = smClient.ImagesChangePosition(smLogin.Session.id, smugPhotoId, j + 1);
                        Debug.Assert(done);
                        UpdatePhotoStatus(currentAlbum, currentAlbum.photos[j]);
                        Console.WriteLine("done!");
                    }
                    else
                    {
                        Console.WriteLine("Got photoId of 0 for photo {0} in KEG album {1} SmugMug album {2}", currentAlbum.photos[j].photoId, currentAlbum.albumName, smugAlbumId);
                    }
                }

                UpdateAlbumStatus(currentAlbum);
            }

            Console.WriteLine();
            Console.WriteLine("Done uploading albums to SmugMug");
        }

        private void UpdateSmugMugAlbumId(KodakAlbum currentAlbum, int smugAlbumId)
        {
            string statusFile = string.Format("{0}\\Keg2Smug.{1}.status", temporaryLocation, currentAlbum.albumId);

            using (FileStream fileStream =  File.Create(statusFile))
            {
                // Write smugmug albumid to the file
                using (StreamWriter writer= new StreamWriter(fileStream))
                {
                    writer.WriteLine("SmugMugAlbumId|{0}", smugAlbumId);
                }

                fileStream.Close();
            }
        }

        private void LoadExistingPhotoList(KodakAlbum currentAlbum, List<string> existingPhotoIds)
        {
            string statusFile = string.Format("{0}\\Keg2Smug.{1}.status", temporaryLocation, currentAlbum.albumId);

            FileStream fileStream = null;
            if (File.Exists(statusFile))
            {
                fileStream = File.OpenRead(statusFile);
            }
            else
            {
                return;
            }

            using (fileStream)
            {
                // Read all photoIds from the file
                using (StreamReader reader = new StreamReader(fileStream))
                {
                    string photoId = reader.ReadLine();
                    while (photoId != null)
                    {
                        if (!photoId.StartsWith("SmugMugAlbumId"))
                        {
                            existingPhotoIds.Add(photoId);
                        }
                        photoId = reader.ReadLine();
                    }
                }

                fileStream.Close();
            }
        }

        private bool SmugMugAlbumExists(KodakAlbum currentAlbum, out int smugAlbumId)
        {
            smugAlbumId = 0;
            string statusFile = string.Format("{0}\\Keg2Smug.{1}.status", temporaryLocation, currentAlbum.albumId);

            if (File.Exists(statusFile))
            {
                using (FileStream fileStream = File.OpenRead(statusFile))
                {
                    // Read the smugmug albumId
                    using (StreamReader reader = new StreamReader(fileStream))
                    {
                        string smugMugAlbumId = reader.ReadLine();
                        if (!smugMugAlbumId.StartsWith("SmugMugAlbumId"))
                        {
                            Console.WriteLine("Unable to find SmugMug album id for album {0}", currentAlbum.albumName);
                            return false;
                        }

                        string[] tokens = smugMugAlbumId.Split('|');
                        return int.TryParse(tokens[1], out smugAlbumId);
                    }
                }
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Once an album is done uploading to SmugMug, record the fact so we can skip it next time
        /// </summary>
        private void UpdateAlbumStatus(KodakAlbum currentAlbum)
        {
            string statusFile = string.Format("{0}\\{1}", temporaryLocation, "Keg2Smug.status");

            FileStream fileStream = null;
            if (File.Exists(statusFile))
            {
                fileStream = File.OpenWrite(statusFile);
            }
            else
            {
                fileStream = File.Create(statusFile);
            }

            using (fileStream)
            {
                // go to the end of the file
                fileStream.Seek(0, SeekOrigin.End);

                // Write the albumId
                using (StreamWriter fileStreamWriter = new StreamWriter(fileStream))
                {
                    fileStreamWriter.WriteLine(currentAlbum.albumId);
                    fileStreamWriter.Flush();
                }

                fileStream.Close();
            }
        }

        /// <summary>
        /// Once a photo has been uploaded, record the fact so we can skip it next time around
        /// </summary>
        private void UpdatePhotoStatus(KodakAlbum currentAlbum, KodakPhoto currentPhoto)
        {
            string statusFile = string.Format("{0}\\Keg2Smug.{1}.status", temporaryLocation, currentAlbum.albumId);

            FileStream fileStream = null;
            if (File.Exists(statusFile))
            {
                fileStream = File.OpenWrite(statusFile);
            }
            else
            {
                fileStream = File.Create(statusFile);
            }

            using (fileStream)
            {
                // go to the end of the file
                fileStream.Seek(0, SeekOrigin.End);

                // Write the albumId
                using (StreamWriter fileStreamWriter = new StreamWriter(fileStream))
                {
                    fileStreamWriter.WriteLine(currentPhoto.photoId);
                    fileStreamWriter.Flush();
                }

                fileStream.Close();
            }
        }

        private int UploadPhoto(KodakAlbum currentAlbum, KodakPhoto photo, int smugAlbumID)
        {
            string filePath = string.Format("{0}\\{1}\\{2}", temporaryLocation, currentAlbum.albumId, photo.fileName);
            SmugUploadResponse response;
            try
            {
                response = smClient.ImagesUploadBinary(filePath, smLogin.Session.id, 0, smugAlbumID.ToString(), null);
                return response.Image.id;
            }
            catch (Exception)
            {
                Console.WriteLine("Failed to upload image {0} to SmugMug", filePath);
                return 0;
            }

            /*

            NameValueCollection queryStringCollection = new NameValueCollection();
            queryStringCollection.Add("method", "smugmug.images.uploadFromURL");
            queryStringCollection.Add("SessionID", smClient.SessionID);
            queryStringCollection.Add("APIKey", API_KEY);
            queryStringCollection.Add("AlbumID", smugAlbumID.ToString());
            queryStringCollection.Add("Caption", photo.photoCaption);
            
            string fullPhotoUri = string.Format("http://www.kodakgallery.com/servlet/FullResDownload?collid={0}&photoid={1}&UV={2}", currentAlbum.albumId, photo.photoId, uvkey);

            queryStringCollection.Add("URL", fullPhotoUri);

            string baseUri = "http://api.smugmug.com/hack/rest/1.2.0/";

            string postBody = "";

            foreach (string key in queryStringCollection.AllKeys)
            {
                if (postBody.Length > 0)
                {
                    postBody = string.Format("{0}&{1}={2}", postBody, key, HttpUtility.UrlEncode(queryStringCollection[key]));
                }
                else
                {
                    postBody = string.Format("{0}={1}", key, HttpUtility.UrlEncode(queryStringCollection[key]));
                }
            }

            string response = null;
            HttpStatusCode statusCode = HttpPost(baseUri, smugUserAgent, postBody, out response);
            if (statusCode == HttpStatusCode.OK)
            {
                Match match = imageIdRegex.Match(response);
                if (match.Success)
                {
                    string imageIdString = match.Groups[1].Captures[0].Value;
                    int imageId = int.Parse(imageIdString);
                    return imageId;
                }
                else
                {
                    Console.WriteLine("Could not parse imageId from SmugMug response for photo {0} in album {1}", photo.photoId, currentAlbum.albumId);
                    Console.WriteLine("Response from SmugMug was {0}", response);
                    return 0;
                }
            }
            else
            {
                Console.WriteLine("Failed to upload photo {0} in album {1}", photo.photoId, currentAlbum.albumId);
                Console.WriteLine("Response from server was {0}", statusCode);
            }
            return 0;
            */
        }

        /// <summary>
        /// Get the photo from Kodak
        /// </summary>
        private void GetPhoto(KodakAlbum currentAlbum, KodakPhoto currentPhoto)
        {
            // If the photo has already been fetched, do nothing
            if (File.Exists(string.Format("{0}\\{1}\\{2}.jpg", temporaryLocation, currentAlbum.albumId, currentPhoto.photoId)))
            {
                Console.WriteLine("already exists!");
                return;
            }

            if (!testForKodakPremierMember)
            {
                // Check if the user is a premier kodak gallery user by trying to download the high-res photo ourselves
                TestForKodakPremierMember(currentAlbum, currentPhoto);
            }

            string fullPhotoUri = string.Format("http://www.kodakgallery.com/servlet/FullResDownload?collid={0}&photoid={1}", currentAlbum.albumId, currentPhoto.photoId);

            HttpStatusCode statusCode = HttpStatusCode.Unused;
            string errorString = null;
            try
            {
                HttpWebRequest photoRequest = (HttpWebRequest)WebRequest.Create(fullPhotoUri);
                photoRequest.Method = "GET";
                photoRequest.UserAgent = kegUserAgent;
                photoRequest.CookieContainer = new CookieContainer();
                photoRequest.CookieContainer.Add(this.kodakLoginCookies);

                using (HttpWebResponse photoResponse = (HttpWebResponse)photoRequest.GetResponse())
                {
                    if (photoResponse.StatusCode == HttpStatusCode.OK)
                    {
                        int contentLength = (int)photoResponse.ContentLength;
                        byte[] buffer = new byte[contentLength];
                        int bytesRead = 0;
                        using (Stream stream = photoResponse.GetResponseStream())
                        {
                            while (bytesRead < contentLength)
                            {
                                int numBytesToRead = (contentLength - bytesRead) < 64000 ? (contentLength - bytesRead) : 64000;
                                bytesRead += stream.Read(buffer, bytesRead, numBytesToRead); // Read 64KB chunks
                            }
                            Console.WriteLine("{0:D9}/{1} bytes....done!", bytesRead, contentLength);
                        }

                        string fileName = string.Format("{0}.jpg", currentPhoto.photoId);
                        File.WriteAllBytes(string.Format("{0}\\{1}\\{2}.jpg", temporaryLocation, currentAlbum.albumId, currentPhoto.photoId), buffer);
                        currentPhoto.fileName = fileName;
                    }
                }
            }
            catch (WebException wex)
            {
                using (HttpWebResponse response = (HttpWebResponse)wex.Response)
                {
                    statusCode = response.StatusCode;
                    Console.WriteLine("Failed to fetch photo {0} in album {1} due to error {2}", currentPhoto.photoId, currentAlbum.albumId, statusCode);
                    return;
                }
            }
            catch (Exception ex)
            {
                errorString = ex.ToString();
                Console.WriteLine("Failed to fetch photo {0} in album {1} due to error {2}", currentPhoto.photoId, currentAlbum.albumId, errorString);
                return;
            }
        }

        private void TestForKodakPremierMember(KodakAlbum currentAlbum, KodakPhoto photo)
        {
            string fullPhotoUri = string.Format("http://www.kodakgallery.com/servlet/FullResDownload?collid={0}&photoid={1}", currentAlbum.albumId, photo.photoId);

            HttpStatusCode statusCode = HttpStatusCode.Unused;
            string errorString = "";

            try
            {
                HttpWebRequest photoRequest = (HttpWebRequest)WebRequest.Create(fullPhotoUri);
                photoRequest.Method = "GET";
                photoRequest.UserAgent = kegUserAgent;
                photoRequest.CookieContainer = new CookieContainer();
                photoRequest.CookieContainer.Add(this.kodakLoginCookies);

                using (HttpWebResponse photoResponse = (HttpWebResponse)photoRequest.GetResponse())
                {
                    statusCode = photoResponse.StatusCode;
                }
            }
            catch (WebException wex)
            {
                using (HttpWebResponse response = (HttpWebResponse)wex.Response)
                {
                    statusCode = response.StatusCode;
                }
            }
            catch (Exception ex)
            {
                errorString = ex.ToString();
            }

            if (statusCode == HttpStatusCode.OK)
            {
                testForKodakPremierMember = true;
                return;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Uh-oh! Looks like youre not a Kodak Easyshare Gallery premier member!");
                Console.WriteLine("Request to Kodak Easyshare gallery failed with error {0}", (statusCode == HttpStatusCode.Unused) ? errorString : statusCode.ToString());
                Environment.Exit(9);
            }
        }

        private string ReadInputString(string prompt, bool allowEmpty)
        {
            string input = null;
            do
            {
                Console.WriteLine();
                Console.Write(prompt);
                input = Console.ReadLine();
            } while (input == null || input.Length == 0 && !allowEmpty);

            return input;
        }

        private char ReadInputChar(string prompt)
        {
            Console.WriteLine();
            Console.Write(prompt);
            return Console.ReadKey().KeyChar;
        }

        private HttpStatusCode HttpGet(string uri, string userAgent, out string response)
        {
            WebHeaderCollection outgoingHeaders = new WebHeaderCollection();
            WebHeaderCollection incomingHeaders = null;
            CookieCollection incomingCookies = null;

            return HttpGet(uri, outgoingHeaders, this.kodakLoginCookies,  userAgent, out incomingHeaders, out incomingCookies, out response);
        }


        private HttpStatusCode HttpGet(string uri, 
                                       WebHeaderCollection outgoingHeaders,
                                       CookieCollection outgoingCookies,
                                       string userAgent,
                                       out WebHeaderCollection incomingHeaders,
                                       out CookieCollection incomingCookies,
                                       out string response)
        {
            response = null;
            incomingHeaders = null;
            incomingCookies = null;

            try
            {
                HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(uri);
                webRequest.Method = "GET";
                webRequest.UserAgent = userAgent;
                webRequest.CookieContainer = new CookieContainer();
                if (outgoingCookies != null)
                {
                    webRequest.CookieContainer.Add(outgoingCookies);
                }
                foreach (string header in outgoingHeaders.AllKeys)
                {
                    webRequest.Headers.Add(header, outgoingHeaders[header]);
                }
                
                using (HttpWebResponse webResponse = (HttpWebResponse)webRequest.GetResponse())
                {
                    if (webResponse.StatusCode == HttpStatusCode.OK)
                    {
                        incomingHeaders = webResponse.Headers;
                        incomingCookies = webResponse.Cookies;
                        StreamReader reader = new StreamReader(webResponse.GetResponseStream());
                        response = reader.ReadToEnd();
                        return HttpStatusCode.OK;
                    }
                    else
                    {
                        return webResponse.StatusCode;
                    }
                }
            }
            catch (WebException wex)
            {
                if (wex.Response != null)
                {
                    using (HttpWebResponse webResponse = (HttpWebResponse)wex.Response)
                    {
                        return webResponse.StatusCode;
                    }
                }
                else
                {
                    return HttpStatusCode.PaymentRequired;
                }
            }
        }

        private HttpStatusCode HttpPost(string uri, string userAgent, string body, out string response)
        {
            WebHeaderCollection outgoingHeaders = new WebHeaderCollection();
            CookieCollection outgoingCookies = new CookieCollection();
            WebHeaderCollection incomingHeaders = null;
            CookieCollection incomingCookies = null;
            return HttpPost(uri, outgoingHeaders, outgoingCookies, null, userAgent, body, out incomingHeaders, out incomingCookies, out response);
        }


        private HttpStatusCode HttpPost(string uri,
                                        WebHeaderCollection outgoingHeaders,
                                        CookieCollection outgoingCookies,
                                        string referer,
                                        string userAgent,
                                        string body,
                                        out WebHeaderCollection incomingHeaders,
                                        out CookieCollection incomingCookies,
                                        out string response)
        {
            response = null;
            incomingHeaders = null;
            incomingCookies = null;

            try
            {
                HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(uri);
                webRequest.Method = "POST";
                webRequest.UserAgent = userAgent;
                webRequest.ContentType = "application/x-www-form-urlencoded";
                webRequest.ContentLength = body.Length;
                webRequest.Accept = "image/gif, image/x-xbitmap, image/jpeg, image/pjpeg, application/x-shockwave-flash, application/xaml+xml, application/vnd.ms-xpsdocument, application/x-ms-xbap, application/x-ms-application, application/vnd.ms-excel, application/vnd.ms-powerpoint, application/msword, application/x-silverlight, */*";
                webRequest.Headers.Add("Accept-Language", "en-us");
                webRequest.AllowAutoRedirect = false;
                webRequest.Referer = referer;
                webRequest.Headers.Add(outgoingHeaders);
                webRequest.CookieContainer = new CookieContainer();
                webRequest.CookieContainer.Add(outgoingCookies);

                using (Stream requestStream = webRequest.GetRequestStream())
                {
                    using (StreamWriter writer = new StreamWriter(requestStream))
                    {
                        writer.Write(body);
                    }
                }

                using (HttpWebResponse webResponse = (HttpWebResponse)webRequest.GetResponse())
                {
                    if (webResponse.StatusCode == HttpStatusCode.OK || webResponse.StatusCode == HttpStatusCode.Found)
                    {
                        StreamReader reader = new StreamReader(webResponse.GetResponseStream());
                        response = reader.ReadToEnd();
                        incomingHeaders = webResponse.Headers;
                        incomingCookies = webResponse.Cookies;
                        return HttpStatusCode.OK;
                    }
                    else
                    {
                        return webResponse.StatusCode;
                    }
                }
            }
            catch (WebException wex)
            {
                if (wex.Response != null)
                {
                    using (HttpWebResponse webResponse = (HttpWebResponse)wex.Response)
                    {
                        return webResponse.StatusCode;
                    }
                }
                else
                {
                    return HttpStatusCode.PaymentRequired;
                }
            }
        }


        private void ShowUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Keg2Smug - Migrate your photo albums from Kodak Easyshare Gallery to SmugMug");
            Console.WriteLine();
            Console.WriteLine("Usage: keg2smug.exe options");
            Console.WriteLine("Options: -nd - do not migrate the description from the Kodak Easyshare Gallery albums.");
            Console.WriteLine("         -nc - do not migrate the photo captions from the Kodak Easyshare Gallery albums.");
            Console.WriteLine();
            Console.WriteLine("Sample usage: keg2smug.exe -nd");
            Console.WriteLine("Sample usage: keg2smug.exe -nd -nc");
        }


    }
}
