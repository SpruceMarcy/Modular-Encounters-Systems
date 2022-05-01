﻿using Sandbox.Common.ObjectBuilders.Definitions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;

namespace ModularEncountersSystems.Files {

	public struct DataPadEntry {

		public string DataPadTitle;
		public string DataPadBody;

		public DataPadEntry(string title, string body) {

			DataPadTitle = title;
			DataPadBody = body;

		}

		public MyObjectBuilder_Datapad BuildDatapad() {

			MyObjectBuilder_Datapad datapad = new MyObjectBuilder_Datapad();
			datapad.Name = GetTitle();
			datapad.Data = GetBody();
			datapad.SubtypeName = "Datapad";
			return datapad;

		}

		public string GetTitle() {

			return TextTemplate.CleanString(DataPadTitle);

		}

		public string GetBody() {

			return TextTemplate.CleanString(DataPadBody);

		}

		

	}

	public struct LcdEntry {

		public string TextSurfaceBlockName;
		public int TextSurfaceIndex;

		public bool ApplyLcdText;
		public string LcdText;

		public bool ApplyLcdImage;
		public string[] LcdImages;
		public float LcdImageChangeDelay;

		public LcdEntry(bool dummy = false) {

			TextSurfaceBlockName = "";
			TextSurfaceIndex = -1;

			ApplyLcdText = false;
			LcdText = "";

			ApplyLcdImage = false;
			LcdImages = new string[] { };
			LcdImageChangeDelay = 1;

		}
	
	}

	public class TextTemplate {

		public string Name;
		public string Title;
		public string Description;
		public string BlockName;
		public string CustomData;

		[XmlArrayItem("LcdEntry")]
		public LcdEntry[] LcdEntries;

		[XmlArrayItem("DataPadEntry")]
		public DataPadEntry[] DataPadEntries;

		public TextTemplate() {

			Name = "";
			Title = "";
			Description = "";
			BlockName = "";
			CustomData = "";
			LcdEntries = new LcdEntry[] { };
			DataPadEntries = new DataPadEntry[] { };

		}

		public static string CleanString(string str) {

			var sb = new StringBuilder();
			char[] delims = new[] { '\r', '\n' };
			string[] strings = str.Split(delims, StringSplitOptions.RemoveEmptyEntries);

			foreach (var item in strings) {

				sb.Append(item?.Trim() ?? "").AppendLine();

			}

			return sb.ToString();

		}

	}

}
