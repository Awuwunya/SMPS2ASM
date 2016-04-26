﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using System.Data;
using NCalc;
using System.Diagnostics;

namespace smps2asm {
	class Program {
		public static Dic sett;
		public static string name, lblparent = null;
		public static List<OffsetString> lines, lables;
		public static uint offset = 0, boff = 0;
		public static byte[] dat;
		public static bool[] skippedBytes;
		public static bool followlable = false, inHeader = true, stop = false;
		private static Dic[] currDics;

		static void Main(string[] args) {
			Stopwatch timer = null;
			bool cmd;
			string settings, fout, fin, 
				folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(System.Environment.CurrentDirectory), @""));

			// no commandline arguments, get arguments from user
			if (args.Length == 0) {
				cmd = false;
				fin = folder +"\\music\\"+ GetInput("tell the music file name with extension");
				settings = folder + "\\" + GetInput("\ntell the sound driver folder name") + "\\smps2asm script.asm";
				name = GetInput("\ntell the project name");

			// 3 or more commandline argumets, get arguments from the commandline
			} else if(args.Length >= 3) {
				cmd = true;
				fin = folder + "\\music\\" + args[0];
				settings = folder + "\\" + args[1] +"\\smps2asm script.asm";
				name = args[2];

			// 1 or 2 arguments, show usage to user
			} else {
				Console.WriteLine("Illegal number of arguments!");
				error("Usage: smps2asm <filename.extension> <driver folder> <project name>");
				return;
			}

			// removes the extension of input file and adds .asm as the extension of output file
			if (fin.IndexOf(".", fin.LastIndexOf("\\")) > 0) {
				fout = fin.Substring(0, fin.LastIndexOf(".")) + ".asm";

			} else {
				fout = fin + ".asm";
			}

			// makes sure necessary files exist
			if (!File.Exists(settings)) {
				error("File '" + settings + "' does not exist!");
			}

			if (!File.Exists(fin)) {
				error("File '" + fin + "' does not exist!");
			}

			// parse settings file
			try {
				Console.WriteLine("Parsing script...");
				parseSettings(File.ReadAllText(settings).Replace("\t", "").Replace("\r", ""), args.Skip(3).ToArray());
			} catch (Exception e) {
				error("Could not parse script: ", e);
			}
			
			if (!cmd) {
				timer = new Stopwatch();
				timer.Start();
			}

			lables = new List<OffsetString>();
			lines = new List<OffsetString>();

			// open input file for reading
			try {
				if (((Dic) sett.GetValue("dat")).ContainsKey("offset")) {
					offset = parseUInt((string) ((Dic) sett.GetValue("dat")).GetValue("offset"), -1);
				}
				
				// read all bytes from input file
				dat = File.ReadAllBytes(fin);
				// create array of bools which determines if byte is skipped or not.
				skippedBytes = new bool[dat.Length];
				// Now, translate the file into data lines of code.
				TranslateFile();

			} catch (Exception e) {
				error("Could not read input file: ", e);
			}

			// here, we write out the file, check for unused bytes, and create lables.
			try {
				if (!File.Exists(fout)) {
					File.Create(fout).Close();
				}

				Console.WriteLine("Writing out the file...");
				// default starting string is as follows:
				/*
					[name]_Header:
						smpsHeaderStartSong

				*/
				// it is done to fix any crashes or inconsistency with including this macro,
				// because it does not take a single byte of data, and therefore we need to
				// either pretend we are in address -1, or have 2 entries for same byte.
				// both situations are very bad for the code below, because it does not know
				// how to handle checking for unused bytes in negative address, or it thinks
				// there are multiple data in same location.
				string o = name + "_Header:\r\n\tsmpsHeaderStartSong\r\n", currLine = "";
				uint currLineArg = 0, nxtchk = 0;
				bool lastwaslable = true;

				// now starts the main loop which will search for all the lables
				for (int x = (int) offset;x < offset + dat.Length;x++) {
					// fake line to be replaced by current line when done. Used to also check if 2 different lines overlap
					OffsetString ln = new OffsetString(ulong.MaxValue, 1, "<<<<<<<<<<<");
					foreach (OffsetString o1 in lines) {
						// check if this line should go here
						if ((int) o1.offset == x && !ln.line.Equals(o1.line)) {
							// checks if there is another line found already. Warns the user if so
							if (ln.length > 0 && (ulong)x == ln.offset) {
								Console.WriteLine("Warning: Could not decide line at " + toHexString(x, 4) + "! '" + ln.line + "' vs '" + o1.line + "'");
							}

							ln = o1;
							// check if this is data
							if (o1.line.StartsWith("db ")) {
								// if there were not already data line started, start a new one
								if (currLineArg == 0) {
									currLine = "\tdc.b ";

								// if there was 8 or more bytes in this line, start new line
								} else if (currLineArg >= 8) {
									o += currLine.Substring(0, currLine.Length - 2) + "\r\n";
									currLineArg = 0;
									currLine = "\tdc.b ";
								}

								// then finally add the byte to the line
								currLine += o1.line.Substring(3) + ", ";
								currLineArg++;

							} else {
								// split byte line if there was one
								if (currLineArg > 0) {
									o += currLine.Substring(0, currLine.Length - 2) + "\r\n";
									currLineArg = 0;
								}

								// add the line to file
								o += o1.line + "\r\n";
								lastwaslable = false;
							}
						}
					}

					// checks if no line was found for this byte,
					// last line did not have its bytes extend here,
					// and this byte was not in skipped bytes list
					if (ln.line.Equals("<<<<<<<<<<<") && x >= nxtchk && !skippedBytes[x - offset]) {
						if (currLineArg == 0) {
							currLine = "\t; Unused\r\n\tdc.b ";

						} else if (currLineArg >= 8) {
							o += currLine.Substring(0, currLine.Length - 2) + "\r\n";
							currLineArg = 0;
							currLine = "\tdc.b ";
						}
						
						currLine += toHexString(ReadByte((uint)(x - offset)), 2) +", ";
						currLineArg++;
					}

					foreach (OffsetString o1 in lables) {
						if ((int) o1.offset == x + 1) {
							if (currLineArg > 0) {
								o += currLine.Substring(0, currLine.Length - 2) + "\r\n";
								currLineArg = 0;
							}

							if (lastwaslable) {
								o += o1.line + "\r\n";

							} else {
								o += "\r\n" + o1.line + "\r\n";
							}

							lastwaslable = true;
						}
					}

					if (!ln.line.Equals("<<<<<<<<<<<") && ln.length > 0) {
						nxtchk = (uint)((uint)x + ln.length);
					}
				}

				File.WriteAllText(fout, o);

			} catch (UnauthorizedAccessException) {
				error("Could not create output file: Insufficient permissions");

			} catch (IOException e) {
				error("Could not create output file: IOException; "+ e.Message);

			} catch (Exception e) {
				error("Could not create output file: ", e);
			}

			if (!cmd) {
				timer.Stop();
				error("SMPS2ASM conversion successful! Took "+ timer.ElapsedMilliseconds +" ms!");
			}
		}

		private static void TranslateFile() {
			object h = sett.GetValue("header");
			if (h != null && h.GetType() == typeof(Dic)) {
				Console.WriteLine("Parsing header...");
				parseAllFunctions2((Dic)h);

			} else {
				error("SMPS files without a header are not supported!");
			}

			inHeader = false;
			followlable = true;
			foreach (OffsetString o in lables.ToArray()) {
				Console.WriteLine("Parsing " + o.line + "...");
				if (o.line.Contains("DAC")) {
					boff = (uint) (o.offset - offset);
					parseInput(new Dic[] { sett.GetValue("coordination") as Dic, sett.GetValue("DAC") as Dic });

				} else if (o.line.Contains("FM") || o.line.Contains("PSG")) {
					boff = (uint) (o.offset - offset);
					parseInput(new Dic[] { sett.GetValue("coordination") as Dic, sett.GetValue("note") as Dic });

				} else if (o.line.Contains("Voices")) {
					// find the closest lable after the voices lable. Fixes Voices overflowing to other SMPS code
					// this happens in WOI music, when voices are before the SMPS code
					ulong loff = 0xFFFFFFFFFFFFFFFF;
					foreach (OffsetString of in lables.ToArray()) {
						if(of.offset > o.offset && of.offset < loff) {
							loff = of.offset;
						}
					}

					// this converts the Z80 offset to offset in input file. In 68k offset is 0 so its not affected.
					loff -= offset;

					boff = (uint)(o.offset - offset);
					for (uint i = boff;i < dat.Length && i < loff;) {
						parseAllFunctions2(sett.GetValue("Voices") as Dic);
						// this simply marks all the bytes as unused. Not worth the headache to fix this properly
						for (;i < boff;i ++) skippedBytes[i] = true;
						i = boff;
					}
				}
			}
		}

		private static void parseInput(Dic[] dic) {
			currDics = dic;
		again:;
			bool b = true;
			while (boff < dat.Length && b) {
				uint off = boff;

				foreach (Dic d in dic) {
					b = parseAllFunctions(d);
					if (!b) break;
					if (boff != off) goto again;
				}

				if (!inHeader && boff == off) {
					PutOnLine(""+ ReadByte(boff));
					boff++;
				}
			}
			stop = false;
		}

		private static bool parseAllFunctions(Dic d) {
			if (boff >= dat.Length) {
				// havent tested this properly, so having warning for now
				Console.WriteLine("WARN: Checkings functions after end of SMPS file! (size: " + dat.Length + ", offset: "+ boff +") Please report to Natsumi if this causes a crash!");
		//		error("Could not resolve file: Out of file bounds (size: " + dat.Length + ", offset: "+ boff +')');
			}

			uint off = boff;
			foreach (KeyValuePair<string, object> kv in d.GetKeyset()) {
				parseFunction(kv.Key, kv.Value, d, true);
				if(stop) return false;
				if (!inHeader && boff != off) return true;
			}
			
			return true;
		}

		private static void parseAllFunctions2(Dic d) {
			foreach (KeyValuePair<string, object> kv in d.GetKeyset()) {
				parseFunction(kv.Key, kv.Value, d, false);
			}
		}

		private static void parseFunction(string key, object value, Dic parent, bool writeEqu) {
		//	Console.WriteLine(ReadByte(boff) + " " + boff);
			if (value.GetType() == typeof(Equate)) {
				// parse equate
				Equate v = (Equate) value;
				if (!v.calculated) v.calc();

				if (writeEqu && ReadByte(boff) == v.value) {
					PutOnLine(key);

					boff++;
				}

			} else if (value.GetType() == typeof(EquateChange)) {
				// change equate value
				Equate e = parent.GetValue(((EquateChange) value).name) as Equate;
				if(e == null) {
					error("Could not resolve file: Could not find equate '"+ ((EquateChange) value).name + "'!");
				}

				// plop the new value in and calculate it!
				e.raw = ((EquateChange) value).value;
				e.calc();

			} else if (value.GetType() == typeof(Condition)) {
				// condition
				Condition v = (Condition) value;
				if (parseBool(v.GetCondition(), -1)) {
					parseAllFunctions2(v.True);

				} else {
					parseAllFunctions2(v.False);
				}

			} else if (value.GetType() == typeof(Command)) {
				// we just need to emulate a translation, and then ignore the result!
				// easy right? Just remember to not use 'aw' or 'ow', that will screw things up!
				TranslateAllAbstract(((Command) value).command);

			} else if (value.GetType() == typeof(Repeat)) {
				// repeat block
				Repeat v = (Repeat) value;
				for(int i = 0; i < parseLong(v.times, -1);i++) {
					parseAllFunctions2(v);
				}

			} else if (value.GetType() == typeof(ChangeLable)) {
				// change lable format
				lblparent = ((ChangeLable) value).lable;

			} else if (value.GetType() == typeof(Comment)) {
				// insert a comment
				uint xoff = boff;
				string ln = ((Comment) value).v;
				while (ln.Contains('{')) {
					int i = ln.IndexOf('{') + 1, l = ln.IndexOf('}', i);
					string res;

					if (l <= 0) {
						res = parseNumber(ln.Substring(i, ln.Length - i), -1);
						ln = ln.Substring(0, i - 1) + res;

					} else {
						res = parseNumber(ln.Substring(i, l - i), -1);
						ln = ln.Substring(0, i - 1) + res + ln.Substring(l + 1);
					}
				}

				AddLable(xoff + offset,'\t' + ln.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\r", "\r"));

			} else if (value.GetType() == typeof(Goto)) {
				// goto file position
				uint off = parseUInt(((Goto) value).off, -1);
				switch (((Goto) value).dir) {
					// absolute
					case 'a':
						boff = off - offset;
						break;

					// forwards
					case 'f':
						boff = (uint)((boff + off) % dat.Length);
						break;

					// backwards
					case 'b':
						boff = (uint) ((boff - off) % dat.Length);
						break;

					default:
						// something has gone terribly wrong
						error("Could not resolve file: Go to type '"+ ((Goto) value).dir + "' not recognized!");
						break;
				}

			} else if (value.GetType() == typeof(Macro) && checkMacroArgs((Macro)value)) {
				// macro block
				Macro v = (Macro) value;
				uint xoff = boff;
				boff += (uint)v.requiredBytes();

				string arg = "", comment;
				int i = 0;
				foreach (string s in v.arguments) {
					if (s != null && s.Length > 0) {
						string res = TranslateAllAbstract(s);
						long r = 0;
						if (!Int64.TryParse(res, out r)) {
							if (isEquation(res)) {
								r = parseLong(res, -1);

							} else {
								arg += ", " + res;
								goto skip;
							}
						}

						if (r <= 0xFF && r >= 0) {
							arg += ", " + toHexString(r, 2);

						} else if (r <= 0xFFFF && r >= 0) {
							arg += ", " + toHexString(r, 4);

						} else {
							arg += ", " + toHexString(r, 8);
						}

						skip:;
					}
					i++;
				}

				if((comment = (string)v.GetValue("comment")) == null) {
					comment = "";

				} else {
					comment = "\t; " + comment;
				}
				
				if (arg.Length > 2) {
					OutLine((uint) (offset + xoff), boff - xoff, "\t" + v.getName() + "\t" + arg.Substring(2) + comment);

				} else {
					OutLine((uint) (offset + xoff), boff - xoff, "\t" + v.getName() + "\t" + arg + comment);
				}
				
				parseAllFunctions(v);

			} else if (value.GetType() == typeof(ArgMod)) {
				// argument modifier
				ArgMod v = (ArgMod) value;
				OffsetString ln = lines.ElementAt(lines.Count - 1);
				int i = ln.line.LastIndexOf('\t') + 1;
				string[] args = ln.line.Substring(i, ln.line.Length - i).Split(',');

				if (v.ID < args.Length) {
					string x = FindValue(parseLong(args[v.ID].Replace("$", "0x"), -1), v);
					if (x != null && x.Length > 0) {
						args[v.ID] = " "+ x;
					}

					string o = ln.line.Substring(0, i);
					args[0].Replace(" ", "");
					foreach(string a in args) {
						o += a +",";
					}

					ln.line = o.Substring(0, o.Length - 1);

				} else {
					error("Not enough arguments in target function; "+ v.ID +" expected, "+ args.Length +" found!");
				}

			} else if (value.GetType() == typeof(Stop)) {
				stop = true;
				return;

			}
		}

		private static void PutOnLine(string value) {
			long r;
			if (Int64.TryParse(value, out r)) {
				value = toHexString(r, 2);
			}

			OutLine(offset + boff, 1, "db "+ value);
		}

		private static bool checkMacroArgs(Macro m) {
			byte[] bytes = new byte[m.requiredBytes()];
			for(int i = 0;i < m.requiredBytes();i++) {
				bytes[i] = ReadByte((uint)(boff + i));
			}

			return m.isThisFlag(bytes);
		}

		private static string toHexString(long res, int zeroes) {
			return "$"+ string.Format("{0:x" + zeroes + "}", res).ToUpper();
		}

		private static string toBinaryString(long res, int zeroes) {
			return "%" + Convert.ToString(res, 2).PadLeft(zeroes);
		}

		private static string FindEquate(string name, Dic d) {
			foreach (KeyValuePair<string, object> kv in d.GetKeyset()) {
				if (kv.Value.GetType() == typeof(Dic)) {
					string r = FindEquate(name, kv.Value as Dic);
					if (r != null) return r;

				} else if (name.Equals(kv.Key)) {
					if (!((Equate) kv.Value).calculated) ((Equate) kv.Value).calc();
					return ""+ ((Equate) kv.Value).value;

				}  
			}

			return null;
		}

		private static string FindValue(long val, Dic d) {
			foreach (KeyValuePair<string, object> kv in d.GetKeyset()) {
				if (kv.Value.GetType() == typeof(Dic)) {
					string r = FindEquate(name, kv.Value as Dic);
					if (r != null) return r;

				} else if (kv.Value.GetType() == typeof(Equate)) {
					if (!((Equate) kv.Value).calculated) ((Equate) kv.Value).calc();

					if (val == ((Equate) kv.Value).value) {
						return kv.Key;
					}
				}
			}

			return null;
		}

		private static string TranslateAllAbstract(string s) {
			try {
				while (s.Contains("/")) {
					int i = s.IndexOf("/") + 1;
					string tr = TranslateAbstract(s.Substring(i, 2));
					s = s.Substring(0, i - 1) + tr + s.Substring(i + 2, s.Length - i - 2);
				}

				while (s.Contains("\\")) {
					int i = s.IndexOf("\\") + 1, o = s.IndexOf("\\", i);
					string tr = FindEquate(s.Substring(i, o - i), sett);

					if (tr == null) {
						error("Could not find equate '" + s.Substring(i, o - i) + "'");
					}

					s = s.Substring(0, i - 1) + parseLong(tr, -1) + s.Substring(o + 1, s.Length - o - 1);
				}

				return s;

			} catch(Exception e) {
				error("Could not Translate '"+ s +"': ", e);
				return null;
			}
		}

		private static string TranslateAbstract(string s) {
			switch (s) {
				case "db":
					return ""+ ReadByte(boff++);

				case "lb":
					skipByte(boff - 1);
					return "" + ReadByte(boff - 1);

				case "nb":
					return "" + ReadByte(boff);

				case "sb":
					skipByte(boff++);
					return "";

				case "dw":
					boff += 2;
					return "" + ReadWord(boff - 2);

				case "lw":
					return "" + ReadWord(boff - 2);

				case "nw":
					return "" + ReadWord(boff);

				case "sw":
					skipByte(boff++);
					skipByte(boff++);
					return "";

				case "aw":
					boff += 2;
					return ReadWordAbs(boff - 2);

				case "ow":
					boff += 2;
					return ReadWordOff(boff - 2);

				case "hw":
					boff += 2;
					return ReadWordHdr(boff - 2);

				case "dl":
					boff += 4;
					return "" + ReadLong(boff);

				case "ll":
					return "" + ReadLong(boff - 4);

				case "nl":
					return "" + ReadLong(boff);

				case "sl":
					skipByte(boff++);
					skipByte(boff++);
					skipByte(boff++);
					skipByte(boff++);
					return "";
			}

			error("Could not resolve argument '"+ s +"'");
			return null;
		}

		private static void skipByte(uint off) {
			skippedBytes[off] = true;
		}

		private static byte ReadByte(uint off) {
			return dat[off];
		}

		private static ushort ReadWord(uint off) {
			string endian = ((Equate) ((Dic) sett.GetValue("dat")).GetValue("endian")).raw;

			if (endian.Equals("\"little\"")) {
				return (ushort) ((ReadByte(off)) | ((ReadByte(off + 1) << 8)));

			} else if (endian.Equals("\"big\"")) {
				return (ushort)(((ReadByte(off) << 8)) | (ReadByte(off + 1)));

			} else {
				error("Could not resolve endianness '" + endian.Replace("\"", "") + "'");
				return 0;
			}
		}

		private static string ReadWordAbs(uint off) {
			string endian = ((Equate) ((Dic) sett.GetValue("dat")).GetValue("endian")).raw;
			int x = 0;
			ushort pos;
			string lable = lblparent;

			if (endian.Equals("\"little\"")) {
				pos = (ushort) ((ReadByte(off) | (ReadByte(off + 1) << 8)));

			} else if (endian.Equals("\"big\"")) {
				pos = (ushort) (((ReadByte(off) << 8) | ReadByte(off + 1)));

			} else {
				error("Could not resolve endianness '" + endian.Replace("\"", "") + "'");
				return null;
			}

			while (hasLable(lblparent.Replace("#", "" + (++x) + ':'), pos));
			AddLable(pos, lblparent.Replace("#", "" + x) + ':');
			if (followlable && pos - offset >= boff) {
				uint coff = boff;
				boff = pos - offset;
				parseInput(currDics);
				boff = coff;
			}

			return lblparent.Replace("#", "" + x);
		}

		private static string ReadWordOff(uint off) {
			string endian = ((Equate) ((Dic) sett.GetValue("dat")).GetValue("endian")).raw;
			int x = 0;
			ushort pos;
			string lable = lblparent;

			if (endian.Equals("\"little\"")) {
				pos = (ushort) (boff + (ReadByte(off) | (ReadByte(off + 1) << 8)) - 1);

			} else if (endian.Equals("\"big\"")) {
				pos = (ushort) (boff + ((ReadByte(off) << 8) | ReadByte(off + 1)) - 1);

			} else {
				error("Could not resolve endianness '" + endian.Replace("\"", "") + "'");
				return null;
			}

			while (hasLable(lblparent.Replace("#", "" + (++x) + ':'), pos));
			AddLable(pos, lblparent.Replace("#", "" + x) + ':');

			if (followlable && pos >= boff) {
				uint coff = boff;
				boff = pos - offset;
				parseInput(currDics);
				boff = coff;
			}

			return lblparent.Replace("#", "" + x);
		}

		private static string ReadWordHdr(uint off) {
			string endian = ((Equate) ((Dic) sett.GetValue("dat")).GetValue("endian")).raw;
			int x = 0;
			ushort pos;
			string lable = lblparent;

			if (endian.Equals("\"little\"")) {
				pos = (ushort) (ReadByte(off) | (ReadByte(off + 1) << 8));

			} else if (endian.Equals("\"big\"")) {
				pos = (ushort) ((ReadByte(off) << 8) | ReadByte(off + 1));

			} else {
				error("Could not resolve endianness '" + endian.Replace("\"", "") + "'");
				return null;
			}

			while (hasLable(lblparent.Replace("#", "" + (++x) + ':'), pos));
			AddLable(pos, lblparent.Replace("#", "" + x) +':');

			if (followlable && pos >= boff) {
				uint coff = boff;
				boff = pos;
				parseInput(currDics);
				boff = coff;
			}

			return lblparent.Replace("#", "" + x);
		}

		private static int ReadLong(uint off) {
			string endian = ((Equate) ((Dic) sett.GetValue("dat")).GetValue("endian")).raw;

			if (endian.Equals("\"little\"")) {
				return ReadByte(off) | (ReadByte(off) << 8) | (ReadByte(off) << 16) | (ReadByte(off) << 24);

			} else if (endian.Equals("\"big\"")) {
				return (ReadByte(off) << 24) | (ReadByte(off) << 16) | (ReadByte(off) << 8) | ReadByte(off);

			} else {
				error("Could not resolve endianness '" + endian.Replace("\"", "") + "'");
				return -1;
			}
		}

		private static bool hasLable(string lable, uint off) {
			foreach(OffsetString o in lables) {
				if (lable.Equals(o.line) && o.offset != off) {
					return true;
				}
			}

			return false;
		}

		private static void AddLable(uint pos, string lable) {
			foreach (OffsetString o in lables) {
				if (o.offset == pos && lable.Equals(o.line)) {
					return;
				}
			}

			lables.Add(new OffsetString(pos, 0, lable));
		}

		private static void parseSettings(string data, string[] args) {
			int lnum = 0;
			sett = new Dic(new Dictionary<string, object>());

			// create stack to store stuff into
			LinkedList<Dic> stack = new LinkedList<Dic>();
			stack.AddFirst(sett);

			bool inCondition = false, isTrue = false;
			Condition co = null;
			foreach(string line in data.Split('\n')) {
				lnum++; // keep count of line number

				if (line.Length > 0) {
					switch (line.ElementAt(0)) {
						case '}':
							if (stack.Count < 1) {
								error("smps2asm script.asm:Line " + lnum + ": There is nothing to return to. Seems like this is an extra terminator");
							}

							stack.RemoveFirst();
							if (inCondition && isTrue && line.EndsWith("{")) {
								isTrue = !isTrue;
								stack.AddFirst(co.False);
							}
							break;

						case '=':
							try {
								string name = line.Substring(1, line.IndexOf(' ') - 1);
								if (stack.ElementAt(0).ContainsKey(name)) {
									// HACK WARNING: Create an element to change the value of pre-existing equate.
									// had to do this so no element tries to double-define itself and cause crash.
									// yes it is very hacky but it also works, so fuck off.
									stack.ElementAt(0).Add("EquChange "+ lnum, new EquateChange(name, line.Substring(line.IndexOf(' ') + 1)));

								} else {
									// attempt to add an item to the Dictionary
									stack.ElementAt(0).Add(name, new Equate(line.Substring(line.IndexOf(' ') + 1)));
								}

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.ElementAt(0) + "'! ", e);
							}
							break;

						case '?':
							if(!line.Contains(" {")) {
								error("smps2asm script.asm:Line " + lnum + ": '{' expected");
							}

							Dic d1 = new Dic(new Dictionary<string, object>());
							try {
								stack.ElementAt(0).Add(line.Substring(1, line.IndexOf(' ') - 1), d1);
								stack.AddFirst(d1);

							} catch(Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.ElementAt(0) + "'! ", e);
							}
							break;

						case '!':
							try {
								int namei = line.IndexOf(">") + 2, argi = line.Replace(" ", "").IndexOf(":"),
									endi = line.Contains("{") ? line.Replace(" ", "").IndexOf("{") : line.Replace(" ", "").IndexOf(";");
								string nam = line.Substring(namei, line.IndexOf(":") - namei);
								byte[] flags = parseBytes(line.Substring(1, line.IndexOf(">") - 2), lnum);
								string[] arguments = line.Replace(" ", "").Substring(argi + 1, endi - argi - 1).Split(',');

								Macro d2 = new Macro(flags, nam.Replace("%", ""), arguments, new Dictionary<string, object>());
								stack.ElementAt(0).Add(nam, d2);

								if (line.EndsWith("{")) {
									stack.AddFirst(d2);
								}

							} catch(Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Could not parse line! ", e);
							}
							break;

						case '@':
							string lable = line.Substring(1, line.IndexOf(' ') - 1), ln;
							long num = parseLong(line.Substring(lable.Length + 2, line.IndexOf(' ', lable.Length + 2) - (lable.Length + 2)), lnum);

							if(args.Length <= num) {
								int i = line.IndexOf('"') + 1;
								ln = GetInput(line.Substring(i, line.LastIndexOf('"') - i));

							} else {
								ln = args[num];
							}
							
							try {
								// attempt to add an item to the Dictionary
								stack.ElementAt(0).Add(lable, ln);

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.ElementAt(0) + "'! ", e);
							}
							break;

						case '#':
							try {
								int id = Int32.Parse(line.Substring(1, line.Replace(" ", "").IndexOf('{')));
								ArgMod ar = new ArgMod(new Dictionary<string, object>(), id);
								// attempt to add an item to the Dictionary
								stack.ElementAt(0).Add("ARG "+ id, ar);
								stack.AddFirst(ar);

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.ElementAt(0) + "'! ", e);
							}
							break;

						case '*':
							try {
								Repeat re = new Repeat(line.Substring(2, line.LastIndexOf(' ') - 2), new Dictionary<string, object>());
								// attempt to add an item to the Dictionary
								stack.ElementAt(0).Add("REPT " + lnum, re);
								stack.AddFirst(re);

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.ElementAt(0) + "'! ", e);
							}
							break;

						case '$':
							try {
								Command comm = new Command(line.Substring(2));
								// attempt to add an item to the Dictionary
								stack.ElementAt(0).Add("COMM " + lnum, comm);

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.ElementAt(0) + "'! ", e);
							}
							break;

						case '~':
							try {
								ChangeLable comm = new ChangeLable(line.Substring(1).Replace("£", name));
								// attempt to add an item to the Dictionary
								stack.ElementAt(0).Add("LABL " + lnum, comm);

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.ElementAt(0) + "'! ", e);
							}
							break;

						case ';':
							try {
								// attempt to add an item to the Dictionary
								stack.ElementAt(0).Add("STOP " + lnum, new Stop());

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.ElementAt(0) + "'! ", e);
							}
							break;

						case '¤':
							try {
								co = new Condition(line.Substring(2, line.LastIndexOf(' ') - 2).Replace(" ", ""),
									new Dictionary<string, object>(), new Dictionary<string, object>());
								// attempt to add an item to the Dictionary
								stack.ElementAt(0).Add("CONDITION " + lnum, co);
								stack.AddFirst(co.True);
								isTrue = inCondition = true;

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.ElementAt(0) + "'! ", e);
							}
							break;

						case '%':
							try {
								stack.ElementAt(0).Add("COMMENT " + lnum, new Comment(line.Substring(1)));

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.ElementAt(0) + "'! ", e);
							}
							break;

						case '>':
							try {
								switch (line.ElementAt(1)) {
									case 'a':case 'f':case 'b':
										break;

									default:
										// something has gone terribly wrong
										error("Could not resolve file: Go to type '" + line.ElementAt(1) + "' not recognized!");
										error("smps2asm script.asm:Line " + lnum + ": Go to type '"+ line.ElementAt(1) +"' not recognized!");
										break;
								}
								stack.ElementAt(0).Add("GOTO " + lnum, new Goto(line.ElementAt(1), line.Substring(3)));

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.ElementAt(0) + "'! ", e);
							}
							break;

						default:
							error("smps2asm script.asm:Line " + lnum + ": Symbol not recognized: '"+ line.ElementAt(0) +"'");
							return;
					}
				}
			}

			if(stack.Count > 1) {
				error("smps2asm script.asm:Line " + lnum + ": Script not terminated!");
			}
		}

		private static byte[] parseBytes(string s, int lnum) {
			if (s.Length < 1) return new byte[0];

			byte[] ret = new byte[s.Replace(" ", "").Split(',').Count()];
			int i = 0;
			foreach(string l in s.Replace(" ", "").Split(',')) {
				ret[i] = parseByte(l, lnum);
				i++;
			}

			return ret;
		}

		public static byte parseByte(string s, int lnum) {
			return Byte.Parse(parseNumber(s, lnum));
		}

		public static short parseShort(string s, int lnum) {
			return Int16.Parse(parseNumber(s, lnum));
		}

		public static int parseInt(string s, int lnum) {
			return Int32.Parse(parseNumber(s, lnum));
		}

		public static uint parseUInt(string s, int lnum) {
			return UInt32.Parse(parseNumber(s, lnum));
		}

		public static long parseLong(string s, int lnum) {
			return Int64.Parse(parseNumber(s, lnum));
		}

		public static bool parseBool(string s, int lnum) {
			return bool.Parse(parseNumber(s, lnum));
		}

		private static string parseNumber(string s, int lnum) {
			try {
				char type = '\0';
				int len = 0;

				// if a type is required, check it here
				if (s.Contains("!")) {
					type = s.ElementAt(0);
					len = Int32.Parse(s.Substring(1, s.IndexOf('!') - 1));
					s = s.Substring(s.IndexOf('!') + 1);
				}

				// translate all abstract symbols
				s = TranslateAllAbstract(s);

				// replace any hex constant with decimal
				while (s.Contains("0x")) {
					int i = s.IndexOf("0x") + 2, l = FindNonNumeric(s, i) + 1;
					ulong res;

					if (l <= 0) {
						res = Convert.ToUInt64(s.Substring(i, s.Length - i), 16);
						s = s.Substring(0, i - 2) + res;

					} else {
						res = Convert.ToUInt64(s.Substring(i, l - i), 16);
						s = s.Substring(0, i - 2) + res + s.Substring(FindNonNumeric(s, i) + 1);
					}
				}

				Expression e = new Expression(s);

				// return the type of string requested
				switch (type) {
					// plain
					case '\0':
						return e.Evaluate().ToString();

					// hex
					case '$':
						return toHexString(Int64.Parse(e.Evaluate().ToString()), len);

					// binary
					case '%':
						return toBinaryString(Int64.Parse(e.Evaluate().ToString()), len);

					default:
						error("Uknown return type '" + type + "'! ");
						return null;
				}

			} catch(Exception e) {
				if (lnum != -1) {
					error("smps2asm script.asm:Line " + lnum + ": Cannot evaluate '" + s + "'! ", e);

				} else {
					error("Cannot evaluate '" + s + "'! ", e);
				}
				return "";
			}
		}

		private static int FindNonNumeric(string s, int i) {
			for(;i < s.Length;i++) {
				char a = s.ElementAt(i);

				if(!((a >= '0' && a <= '9') || (a >= 'a' && a <= 'f') || (a >= 'A' && a <= 'F'))) {
					return i - 1;
				}
			}

			return -1;
		}

		private static bool isEquation(string s) {
			for (int i = 0;i < s.Length;i++) {
				char a = s.ElementAt(i);

				if (!((a >= '0' && a <= '9') || (a >= 'a' && a <= 'f') || (a >= 'A' && a <= 'F') || a == 'x' || 
						a == '=' || a == '&' || a == '|' || a == '^' || a == '(' || a == ')' || a == '&' || a == '-' || a == '+' || a == '*' || a == '/' || a == '>' || a == '<' || a == '%')) {
					return false;
				}
			}

			return true;
		}

		private static void OutLine(uint off, ulong len, string ln) {
			lines.Add(new OffsetString(off,len, ln));
		//	Console.WriteLine(toHexString(off, 4) + " " + ln);
		}

		// get input from user, with the string
		private static string GetInput(string str) {
			Console.WriteLine(str);
			string ret;

			do {
				ret = Console.ReadLine();
				if(ret.Length < 1) {
					Console.WriteLine("Length must not be under 1 characters.");
				}

			} while (ret.Length < 1);
			return ret;
		}

		// print error info, and then exits the program after user input is received
		public static void error(string str) {
			Console.Write(str);
			Console.ReadKey(true);
			Environment.Exit(-1);
		}

		public static void error(string str, Exception e) {
			Console.Write(str);
			Console.WriteLine(e.ToString());
			Console.ReadKey(true);
			Environment.Exit(-1);
		}
	}
}
