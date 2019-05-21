using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace ViOpt
{
    public enum ProcessingStatus
    {
        NotProcessed,
        Downloaded,
        Converted,
        Uploaded,
        FixedArticle,
        Processed,
        Corrupted,
        ProcessedWithError
    }
    public class VideoRecord
    {
        public string FileName;
        public string OldSysId;
        public string NewSysId;
        public string ArticleSysId;
        public string FilePath;
        public string NewFilePath;
        public string DownloadLink;
        public string TranslatedSysId;
        public string TranslatedText;
        public ProcessingStatus Status;


    }
    class Program
    {
        public static string InstanceURL = "dev68846.service-now.com";
        public static string DefaultPath = "E:/ServiceNow/";
        public static string ProcessFileName = "Process List.txt";
        public static NetworkCredential Credintials;
        private static void Print(string message)
        {
            Console.WriteLine(message);
        }
        private static HttpWebRequest GetRequest(string method, string uri, string contentType = "application/json")
        {

            HttpWebRequest wr = HttpWebRequest.CreateHttp(uri);
            wr.Credentials = Credintials;
            wr.Method = method;
            wr.Accept = "application/json";
            wr.ContentType = contentType;
            wr.Timeout = 5000000;
            return wr;
        }
        private static List<VideoRecord> GetVideoListFromFile()
        {
            Print("Getting video metadata from file");
            var filePath = Path.Combine(DefaultPath, ProcessFileName);
            if (!File.Exists(filePath))
            {
                return null;
            }
            var json = "";
            using (FileStream fs = new FileStream(filePath, FileMode.OpenOrCreate))
            {
                using (StreamReader sr = new StreamReader(fs))
                {
                    json = sr.ReadToEnd();
                }
            }

            var list = JsonConvert.DeserializeObject<List<VideoRecord>>(json);
            return list;
        }
        private static void UpdateFile(List<VideoRecord> list)
        {
            var filePath = Path.Combine(DefaultPath, ProcessFileName);

            var json = JsonConvert.SerializeObject(list);

            using (FileStream fs = new FileStream(filePath, FileMode.OpenOrCreate))
            {
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    sw.Write(json);
                }
            }
        }
        private static List<VideoRecord> GetVideoList()
        {
            Print($"Getting video metadata from {InstanceURL}");
            var uri =
                $"https://{InstanceURL}/api/now/attachment?sysparm_query=table_name%3Dkb_knowledge%5Econtent_type%3Dvideo%2Fmp4";
            var request = GetRequest("GET", uri);
            var obj = GetResponseJObject(request);
            var recordList = new List<VideoRecord>();
            foreach (var result in obj["result"])
            {
                var videoRecord = new VideoRecord();

                videoRecord.FileName = result["file_name"].ToString();
                videoRecord.OldSysId = result["sys_id"].ToString();
                videoRecord.DownloadLink = result["download_link"].ToString();
                videoRecord.ArticleSysId = result["table_sys_id"].ToString();
                videoRecord.Status = ProcessingStatus.NotProcessed;
                recordList.Add(videoRecord);
            }

            return recordList;

        }
        private static string AddPrefixToFileName(string path)
        {
            var oldName = Path.GetFileNameWithoutExtension(path);
            Guid g = Guid.NewGuid();
            string GuidString = Convert.ToBase64String(g.ToByteArray());
            GuidString = GuidString.Replace("=", "");
            GuidString = GuidString.Replace("+", "");
            var prefix = GuidString.Substring(0, 4);
            var newName = $"{oldName}_{prefix}";
            string newPath = path.Replace(oldName, newName);
            return newPath;
        }
        private static void RequestVideo(VideoRecord record)
        {
            Print($"Requesting for video file");
            HttpWebResponse response = null;
            var request = GetRequest(WebRequestMethods.Http.Get, record.DownloadLink);
            var path = Path.Combine(DefaultPath, record.FileName);
            if (File.Exists(path))
            {
                path = AddPrefixToFileName(path);
                record.FileName = Path.GetFileNameWithoutExtension(path);
            }
            try
            {
                response = (HttpWebResponse)request.GetResponse();
                var rs = response.GetResponseStream();

                FileStream fs = File.Create(path);
                int bufferSize = 1024;
                byte[] buffer = new byte[bufferSize];
                int bytesRead = 0;

                while ((bytesRead = rs.Read(buffer, 0, bufferSize)) != 0)
                {
                    fs.Write(buffer, 0, bytesRead);
                }
                record.FilePath = path;
                record.Status = ProcessingStatus.Downloaded;

            }
            catch (WebException e)
            {
                if (e.Status == WebExceptionStatus.ProtocolError)
                {
                    response = (HttpWebResponse)e.Response;
                    Console.WriteLine("Errorcode: {0}", (int)response.StatusCode);
                }
                else
                {
                    Console.WriteLine("Error: {0}", e.Status);
                }
            }
            finally
            {
                if (response != null)
                {
                    response.Close();
                }
            }
        }
        private static void ConvertVideo(VideoRecord record)
        {

            var newFile_path = Path.Combine(DefaultPath, "output");
            newFile_path = Path.Combine(newFile_path, record.FileName);
            if (File.Exists(newFile_path))
            {
                newFile_path = AddPrefixToFileName(newFile_path);
            }
            var processPath = Path.Combine(DefaultPath, "ffmpeg.exe");
            var argsl = $"-y -i \"{record.FileName}\" -movflags faststart -acodec copy -vcodec copy \"output/{record.FileName}\"";


            var ffmpeg = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg.exe",
                    Arguments = argsl,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            ffmpeg.Start();
            ffmpeg.WaitForExit();
            if (ffmpeg.ExitCode == 0)
            {
                Print($"File converted and plased at path: {newFile_path}");
                record.NewFilePath = newFile_path;
                record.Status = ProcessingStatus.Converted;
            }
        }
        private static void UploadVideo(VideoRecord record)
        {
            Print($"Uploading new video");
            var uri =
                $"https://{InstanceURL}/api/now/attachment/file?table_name=kb_knowledge&table_sys_id={record.ArticleSysId}&file_name={WebUtility.UrlEncode(record.FileName)}";
            var request = GetRequest(WebRequestMethods.Http.Post, uri, "video/mp4");

            var rs = request.GetRequestStream();

            using (FileStream fileStream = new FileStream(record.NewFilePath, FileMode.Open, FileAccess.Read))
            {
                byte[] buffer = new byte[4096];
                int bytesRead = 0;
                while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    rs.Write(buffer, 0, bytesRead);
                }
            }

            var jobj = GetResponseJObject(request);
            if (jobj != null)
            {
                record.NewSysId = jobj["result"]["sys_id"].ToString();
                record.Status = ProcessingStatus.Uploaded;
                Print($"Completed successfully. Attachment sys_id: {record.NewSysId}");
            }

        }
        private static JObject GetResponseJObject(HttpWebRequest request)
        {
            string json = "";
            HttpWebResponse response = null;
            JObject jobj = null;
            Print($"Trying to perform {request.Method} request on :{request.RequestUri} ");
            try
            {
                response = (HttpWebResponse)request.GetResponse();
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    return null;
                }
                StreamReader sr = new StreamReader(response.GetResponseStream());
                json = sr.ReadToEnd();

                jobj = JObject.Parse(json);
                Print($"Request performed successfully!");
            }
            catch (WebException e)
            {
                if (e.Status == WebExceptionStatus.ProtocolError)
                {
                    response = (HttpWebResponse)e.Response;
                    Print($"Errorcode: {(int)response.StatusCode}");
                    StreamReader sr = new StreamReader(response.GetResponseStream());
                    json = sr.ReadToEnd();

                    jobj = JObject.Parse(json);
                    Print(jobj["error"]["message"].ToString());
                    if (response.StatusCode != HttpStatusCode.NotFound)
                        jobj = null;
                }
                else
                {
                    Console.Write("Error: {0}", e.Status);
                }
            }
            finally
            {
                if (response != null)
                {
                    response.Close();
                }
            }

            return jobj;
        }
        private static string GetArticleBody(VideoRecord record)
        {
            Print($"Obtaining article from {InstanceURL}");
            var uri = $"https://{InstanceURL}/api/now/table/kb_knowledge/{record.ArticleSysId}";
            var request = GetRequest(WebRequestMethods.Http.Get, uri);

            var obj = GetResponseJObject(request);
            var text = "";
            if (obj != null)
            {
                text = obj["result"]["text"].ToString();
                Print($"Article obtained successfully.");
            }

            return text;
        }
        private static string ReplaceOldSysIdInText(string text, VideoRecord record)
        {

            if (text.Contains(record.OldSysId))
            {
                Print($"sys_id: {record.OldSysId} is in article. Replacing with {record.NewSysId}");
                text = text.Replace(record.OldSysId, record.NewSysId);
                text = text.Replace("\"", "\\\"").Replace(Environment.NewLine, " ");


            }
            else
            {
                Print($"sys_id: {record.OldSysId} not found in article. Delete action will skipped.");
                text = string.Empty;
                record.Status = ProcessingStatus.ProcessedWithError;
            }

            return text;
        }
        private static void PatchArticle(VideoRecord record, string patchedText, string sys_id = "")
        {
            Print($"Patching article body");
            var sysid = string.IsNullOrEmpty(sys_id) ? record.ArticleSysId : sys_id;
            var uri = $"https://{InstanceURL}/api/now/table/kb_knowledge/{sysid}";

            var request = GetRequest("PATCH", uri);

            var requestBody = $"{{\"text\":\"{patchedText}\"}}";
            Print($"request body: {requestBody}");
            UTF8Encoding encoding = new UTF8Encoding();
            var byteArray = Encoding.ASCII.GetBytes(requestBody);

            request.ContentLength = byteArray.Length;

            Stream dataStream = request.GetRequestStream();
            dataStream.Write(byteArray, 0, byteArray.Length);
            dataStream.Close();
            var jobj = GetResponseJObject(request);
            if (jobj != null)
            {
                Print($"Atricle patched successfully");
                record.Status = ProcessingStatus.FixedArticle;
            }
            else
            {
                Print($"Error occured while patching article!");
                record.Status = ProcessingStatus.Corrupted;
            }
        }
        private static void RemoveOldVideo(VideoRecord record)
        {
            Print($"Removing attachment {record.OldSysId} from table");
            var uri = $"https://{InstanceURL}/api/now/attachment/{record.OldSysId}";
            var request = GetRequest("DELETE", uri);
            var jobj = GetResponseJObject(request);

            if (jobj == null)
            {
                Print($"Removing old video from attachment table");
                record.Status = ProcessingStatus.Processed;
            }

        }
        private static void PatchTranslatedVersion(VideoRecord record)
        {
            Print($"Searching for translated version of article {record.ArticleSysId}");
            var uri = $"https://{InstanceURL}/api/now/table/kb_knowledge?sysparm_query=parent.sys_id%3D{record.ArticleSysId}&sysparm_limit=1";
            var request = GetRequest(WebRequestMethods.Http.Get, uri);

            var jobj = GetResponseJObject(request);
            if (jobj == null)
            {
                Print($"Article {record.ArticleSysId} not translated yet");
                return;
            }
            var sys_id = "";
            var body = "";
            if (jobj["result"].Any())
            {
                Print("Translated version has been found");
                sys_id = jobj["result"][0]["sys_id"].ToString();
                body = jobj["result"][0]["text"].ToString();
                Print($"Translated version sys_id{sys_id}");

                var fixedBody = ReplaceOldSysIdInText(body, record);
                if (string.IsNullOrEmpty(fixedBody))
                {
                    return;
                }
                PatchArticle(record, fixedBody, sys_id);
            }
            else
            {
                Print($"Translated version not found");
            }


        }
        private static void SpecifyDirectory(bool def = false)
        {
            if (def)
            {
                DefaultPath = "D:/pf";
                return;
            }
            var result = false;
            Console.Write("Enter the working directory: ");
            var wd = Console.ReadLine();
            if (Directory.Exists(wd))
            {
                Print("Directory is valid");
                if (File.Exists(Path.Combine(wd, "ffmpeg.exe")))
                {
                    Print("ffmpeg found");
                    DefaultPath = wd;

                }
                else
                {
                    Print($"ffmpeg not found. Check your directory or place ffmpeg.exe in {wd}");
                    SpecifyDirectory();
                }
            }
            else
            {
                Print($"Directory {wd} is not valid. Please insert right one or create it.");
                SpecifyDirectory();
            }

        }
        private static void SpecifyInstance(bool def = false)
        {
            if (def)
            {
                InstanceURL = "dev68846.service-now.com";
                ProcessFileName = $"{InstanceURL}.txt";
                return;
            }
            Console.Write("Insert ServiceNow instance URL:");
            string url = Console.ReadLine();
            Uri uri = null;
            if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out uri))
            {
                InstanceURL = uri.ToString();
                ProcessFileName = $"{InstanceURL}.txt";
            }
            else
                SpecifyInstance();

        }
        private static SecureString ReadPassword()
        {


            SecureString pass = new SecureString();
            Console.Write("Password: ");
            do
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                // Backspace Should Not Work
                if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                {
                    pass.AppendChar(key.KeyChar);
                    Console.Write("*");
                }
                else
                {
                    if (key.Key == ConsoleKey.Backspace && pass.Length > 0)
                    {
                        pass.RemoveAt(pass.Length - 1);
                        Console.Write("\b \b");
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        break;
                    }
                }
            } while (true);
            return pass;
        }
        private static void SpecifyCredintials(bool def = false)
        {
            if (def)
            {
                Credintials = new NetworkCredential("admin", "Z1585212z");
                return;
            }
            var userName = "";
            do
            {
                Console.Write($"Enter username for {InstanceURL}:");
                userName = Console.ReadLine();
            } while (userName.Length <= 1);
            var pwd = ReadPassword();
            Credintials = new NetworkCredential(userName, pwd);
        }
        private static void UpdateArticle(VideoRecord currentRecord)
        {
            var body = GetArticleBody(currentRecord);
            if (!string.IsNullOrEmpty(body))
            {
                var newBody = ReplaceOldSysIdInText(body, currentRecord);
                if (!string.IsNullOrEmpty(newBody))
                {
                    PatchArticle(currentRecord, newBody);

                }
                else Print("Article not need to be updated!");

                PatchTranslatedVersion(currentRecord);
            }
            else
                currentRecord.Status = ProcessingStatus.Corrupted;
        }
        private static void OnlyPatching(List<VideoRecord> list)
        {
            var newList = list.FindAll(a => a.Status == ProcessingStatus.Uploaded);
            foreach (var item in newList)
            {
                UpdateArticle(item);
            }
            Console.ReadLine();
        }
        public static void Main(string[] args)
        {

            bool def = false; //use -> bool def = true; to use default params

            SpecifyDirectory(def);
            SpecifyInstance(def);
            SpecifyCredintials(def);


            Directory.SetCurrentDirectory(DefaultPath);
            List<VideoRecord> recordList = GetVideoListFromFile();
            if (GetVideoListFromFile() == null)
            {
                Print($"File is empty! obtaining video list from API");
                recordList = GetVideoList();
                UpdateFile(recordList);
            }

            foreach (var currentRecord in recordList)
            {
                if (currentRecord.Status == ProcessingStatus.Processed) continue;
                Stopwatch sw = Stopwatch.StartNew();
                Print($"Processing record {currentRecord.FileName} with status: {currentRecord.Status.ToString()}");
                while (currentRecord.Status != ProcessingStatus.Processed)
                {
                    switch (currentRecord.Status)
                    {
                        case ProcessingStatus.NotProcessed:
                            {
                                RequestVideo(currentRecord);
                            }
                            break;
                        case ProcessingStatus.Downloaded:
                            {
                                Print($"Converting {currentRecord.FileName}");
                                ConvertVideo(currentRecord);
                            }
                            break;
                        case ProcessingStatus.Converted:
                            {
                                UploadVideo(currentRecord);
                            }
                            break;
                        case ProcessingStatus.Uploaded:
                            {
                                UpdateArticle(currentRecord);
                            }
                            break;
                        case ProcessingStatus.FixedArticle:
                            {
                                RemoveOldVideo(currentRecord);
                            }
                            break;
                        case ProcessingStatus.Corrupted:
                            {
                                RemoveOldVideo(currentRecord);
                            }
                            break;
                        case ProcessingStatus.Processed:
                            {
                                continue;
                            }
                        case ProcessingStatus.ProcessedWithError:
                            {
                                continue;
                            }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    UpdateFile(recordList);
                }
                sw.Stop();
                Print($"Process for {currentRecord.FileName} completed in {sw.Elapsed.TotalSeconds} seconds ");
            }
            Print($"All files has been processed");
        }

    }
}
