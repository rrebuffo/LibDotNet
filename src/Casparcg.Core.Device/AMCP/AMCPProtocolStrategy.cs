using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using Casparcg.Core.Device;

namespace Casparcg.Core.Amcp
{
	internal class AMCPProtocolStrategy : Casparcg.Core.Network.IProtocolStrategy
    {
		CasparDevice device_ = null;
		AMCPParser parser_ = new AMCPParser();

		internal AMCPProtocolStrategy(CasparDevice device)
		{
			device_ = device;
			parser_.ResponseParsed += new EventHandler<AMCPParserEventArgs>(parser__ResponseParsed);
		}
        
        void parser__ResponseParsed(object sender, AMCPParserEventArgs e)
		{
			//A response is completely parsed
			//Info about it is in the eventArgs
			if (e.Error == AMCPError.None)
			{
				switch (e.Command)
				{
					case AMCPCommand.VERSION:
						device_.OnVersion(e.Data[0]);
						break;
					case AMCPCommand.CLS:
						OnCLS(e);
						break;
					case AMCPCommand.TLS:
						OnTLS(e);
						break;
					case AMCPCommand.INFO:
                        if (e.Subcommand == string.Empty) OnInfo(e);
                        else OnData(e);
						break;
					case AMCPCommand.LOAD:
						device_.OnLoad((string)((e.Data.Count > 0) ? e.Data[0] : string.Empty));
						break;
					case AMCPCommand.LOADBG:
						device_.OnLoadBG((string)((e.Data.Count > 0) ? e.Data[0] : string.Empty));
						break;
					case AMCPCommand.PLAY:
						break;
					case AMCPCommand.STOP:
						break;
					case AMCPCommand.CG:
						break;
					case AMCPCommand.CINF:
						break;
					case AMCPCommand.DATA:
						OnData(e);
						break;
                    case AMCPCommand.THUMBNAIL:
                        device_.OnThumbnailRetrieved(e.Data[0], e.Command.ToString());
                        break;
                    default:
                        OnData(e);
                        break;
				}
			}
			else
            {
                if (e.Command == AMCPCommand.DATA)
                    OnData(e);
                if (e.Command == AMCPCommand.THUMBNAIL)
                {
                    device_.OnThumbnailRetrieved(string.Empty,e.Command.ToString());
                    return;
                }
            }
		}

		private void OnData(AMCPParserEventArgs e)
        {
            if (e.Error == AMCPError.FileNotFound)
            {
                device_.OnDataRetrieved(string.Empty);
                return;
            }

            if (e.Subcommand == "RETRIEVE")
			{
				if (e.Error == AMCPError.None && e.Data.Count > 0)
					device_.OnDataRetrieved(e.Data[0]);
				else
					device_.OnDataRetrieved(string.Empty);
			}
			else if (e.Subcommand == "LIST")
            {
                device_.OnUpdatedDataList(e.Data);
			}
            else
            {
                if (e.Error == AMCPError.None && e.Data.Count > 0)
                device_.OnServerResponded(e.Command.ToString(),e.Subcommand,e.Data);

            }
		}
        
        private void OnTLS(AMCPParserEventArgs e)
		{
			List<TemplateInfo> templates = new List<TemplateInfo>();
			foreach (string templateInfo in e.Data)
			{
                bool isServer22 = (templateInfo.IndexOf('\"') < 0);
                string pathName = isServer22 ? templateInfo : templateInfo.Substring(templateInfo.IndexOf('\"') + 1, templateInfo.IndexOf('\"', 1) - 1);;
				string folderName = "";
				string fileName = "";

                int delimIndex = pathName.LastIndexOf('/'); // 2.0.7
                if (delimIndex == -1)
                    delimIndex = pathName.LastIndexOf('\\'); // 2.0.6

				if (delimIndex != -1)
				{
					folderName = pathName.Substring(0, delimIndex);
					fileName = pathName.Substring(delimIndex + 1);
				}
				else {
					fileName = pathName;
				}

                int nameEndIndex = templateInfo.LastIndexOf('\"');
                int tempStartIndex = (nameEndIndex < 0 || nameEndIndex + 1 > templateInfo.Length) ? -1 : templateInfo.LastIndexOf('\"')+1 ;
                string temp = templateInfo.Substring(tempStartIndex);
                string[] sizeAndDate = temp.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                Int64 size = Int64.Parse(sizeAndDate[0]);
                DateTime updated = DateTime.ParseExact(sizeAndDate[1], "yyyyMMddHHmmss", null);

                if (isServer22) updated = DateTime.MinValue;
                templates.Add(new TemplateInfo(folderName, fileName, size, updated));
            }

            device_.OnUpdatedTemplatesList(templates);
		}
        
        private string ConvertToTimecode(double time, double fps)
        {
            int hour = (int)(time / 3600);
            int minutes = (int)((time - hour * 3600) / 60);
            int seconds = (int)(time - hour * 3600 - minutes * 60);
            int frames = (int)((time - hour * 3600 - minutes * 60 - seconds) * fps);

            return string.Format("{0:D2}:{1:D2}:{2:D2}:{3:D2}", hour, minutes, seconds, frames);
        }
        
        private void OnCLS(AMCPParserEventArgs e)
		{
			List<MediaInfo> clips = new List<MediaInfo>();
			foreach (string mediaInfo in e.Data)
			{
				string pathName = mediaInfo.Substring(mediaInfo.IndexOf('\"') + 1, mediaInfo.IndexOf('\"', 1) - 1);
				string folderName = "";
				string fileName = "";

                int delimIndex = pathName.LastIndexOf('/'); // 2.0.7
                if (delimIndex == -1)
                    delimIndex = pathName.LastIndexOf('\\'); // 2.0.6

				if (delimIndex != -1)
				{
					folderName = pathName.Substring(0, delimIndex);
					fileName = pathName.Substring(delimIndex + 1);
				}
				else
				{
					fileName = pathName;
				}

				string temp = mediaInfo.Substring(mediaInfo.LastIndexOf('\"') + 1);
				string[] param = temp.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                MediaType type = (MediaType)Enum.Parse(typeof(MediaType), param[0]);
                Int64 size = Int64.Parse(param[1]);
                DateTime updated = DateTime.ParseExact(param[2], "yyyyMMddHHmmss", null);

                string timecode = "";
                long frames = 0;
                double fps = 0;
                if (param.Length > 3)
                {
                    string totalFrames = (param[3]=="NaN")? "0" : param[3];
                    string timebase = param[4];

                    frames = long.Parse(totalFrames);
                    fps = double.Parse(timebase.Split('/')[1])/ int.Parse(timebase.Split('/')[0]);

                    double time = frames * (1d / fps);
                    timecode = (frames == 0 || fps == 0)? "00:00:00:00" : ConvertToTimecode(time, fps);
                }

				clips.Add(new MediaInfo(folderName, fileName, type, size, updated, timecode, frames, fps));
			}

			device_.OnUpdatedMediafiles(clips);
		}
        
        void OnInfo(AMCPParserEventArgs e)
        {
            List<ChannelInfo> channelInfo = new List<ChannelInfo>();

            if (e.Data[0] != null && e.Data[0].Substring(0, 1) == "<")
            {
                device_.OnInfoReceived(e.Data[0]);
                return;
            }
            foreach (string channelData in e.Data)
            {
                string[] data = channelData.Split(' ');
                int id = Int32.Parse(data[0]);
//				VideoMode vm = (VideoMode)Enum.Parse(typeof(VideoMode), data[1]);
//				ChannelStatus cs = (ChannelStatus)Enum.Parse(typeof(ChannelStatus), data[2]);
                channelInfo.Add(new ChannelInfo(id, data[1], ChannelStatus.Stopped, ""));
            }

            device_.OnUpdatedChannelInfo(channelInfo);
		}

		#region IProtocolStrategy Members
		public string Delimiter
		{
			get { return AMCPParser.CommandDelimiter; }
		}

		public Encoding Encoding
		{
			get { return System.Text.Encoding.UTF8; }
		}

		public void Parse(string data, Casparcg.Core.Network.RemoteHostState state)
		{
			parser_.Parse(data);
		}
		public void Parse(byte[] data, int length, Casparcg.Core.Network.RemoteHostState state) { throw new NotImplementedException(); }

		#endregion
	}
}
