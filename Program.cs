using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using NCalc;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;
using System.Runtime.InteropServices;

namespace smps2asm {
	class Program {
		#region GLOBAL VARS
		private static readonly Mutex mutex = new Mutex(true, Assembly.GetExecutingAssembly().GetName().CodeBase);
		private static bool _userRequestExit = false;
		private static bool _doIStop = false;
		static HandlerRoutine consoleHandler;
		#endregion

		[DllImport("Kernel32")]
		public static extern bool SetConsoleCtrlHandler(HandlerRoutine Handler, bool Add);

		// A delegate type to be used as the handler routine for SetConsoleCtrlHandler.
		public delegate bool HandlerRoutine(CtrlTypes CtrlType);

		// An enumerated type for the control messages sent to the handler routine.
		public enum CtrlTypes {
			CTRL_C_EVENT = 0,
			CTRL_BREAK_EVENT,
			CTRL_CLOSE_EVENT,
			CTRL_LOGOFF_EVENT = 5,
			CTRL_SHUTDOWN_EVENT
		}

		private static bool ConsoleCtrlCheck(CtrlTypes ctrlType) {
			// Put your own handler here
			switch(ctrlType) {
				case CtrlTypes.CTRL_C_EVENT:
					_userRequestExit = false;
					ctrlc = true;
					break;

				case CtrlTypes.CTRL_BREAK_EVENT:
				case CtrlTypes.CTRL_CLOSE_EVENT:
				case CtrlTypes.CTRL_LOGOFF_EVENT:
				case CtrlTypes.CTRL_SHUTDOWN_EVENT:
					_userRequestExit = true;
					break;
			}

			return true;
		}

		public static Dic sett;
		public static string name, lblparent = null, defl = "", fout;
		public static List<OffsetString> lines, lables;
		public static uint offset = 0, boff = 0, importd = 0, start;
		public static byte[] dat;
		public static bool[] skippedBytes;
		public static bool followlable = false, inHeader = true, stop = false, debug = false;
		private static Dic[] currDics;
		private static bool ctrlc = false, pause = false, specialcomment = false;
		const uint importdmax = 3;

		private static string t_get(string s, int start, int end) {
			return s.Substring(start, end - start);
		}

		private static string t_rmv(string s, int start, int end) {
			return s.Substring(0, start) + s.Substring(end, s.Length - end);
		}

		private static string t_put(string s, int off, string ch) {
			return s.Substring(0, off) + ch + s.Substring(off, s.Length - off);
		}

		private static void t_cct(string s, int start, int end, ConsoleColor fg, ConsoleColor bg) {
			Console.ForegroundColor = fg;
			Console.BackgroundColor = bg;
			Console.Write(t_get(s, start, end));
		}

		private static void t_cct(string s, int x, int y) {
			Console.SetCursorPosition(x, y);
			Console.Write(s);
		}

		[STAThread]
		static void Main(string[] args) {
			Stopwatch timer = null;
			bool cmd = true;
			string settings, fin, folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(System.Environment.CurrentDirectory), @""));
			string message = "Controls:\n  Up/Down - Move between lines\n  ESC - Quit program\n  Enter - Confirm input and continue program\n  F1 - Change whether program pauses when complete\n  F2 - Change whether to write debug info";

			consoleHandler = new HandlerRoutine(ConsoleCtrlCheck);
			SetConsoleCtrlHandler(consoleHandler, true);
			Console.Title = "SMPS2ASM/NAT  Built: "+ new FileInfo(Assembly.GetExecutingAssembly().Location).LastWriteTime.ToShortDateString() +" "+ new FileInfo(Assembly.GetExecutingAssembly().Location).LastWriteTime.ToShortTimeString();

			{
				int index = 0, cpos = 0, cpose = 0;
				string sp;

			fail:
				// no commandline arguments, get arguments from user
				if(args.Length == 0 || !cmd) {
					sp = "";
					for(int i = 32;i < Console.BufferWidth;i++)
						sp += ' ';

					if(cmd) {
						cmd = false;
						args = new string[] { "", "", "", };
					}

				message:
					Console.Clear();
					Console.ForegroundColor = ConsoleColor.Gray;
					Console.BackgroundColor = ConsoleColor.Black;
					t_cct("Music file name with extension: ", 0, 0);
					t_cct("Sound driver folder name:       ", 0, 1);
					t_cct("Project name:                   ", 0, 2);
					t_cct("Pause: " + (pause ? "Yes" : "No"), 0, 3);
					t_cct("Debug: " + (debug ? "Yes" : "No"), 0, 4);
					t_cct(message, 0, 6);

					Console.ForegroundColor = ConsoleColor.White;
					Console.BackgroundColor = ConsoleColor.Black;
					t_cct(args[0], 32, 0);
					t_cct(args[1], 32, 1);
					t_cct(args[2], 32, 2);
					Console.SetCursorPosition(32 + cpos, index);
					goto repaint;

				loop:
					while(!Console.KeyAvailable) {
						Thread.Sleep(10);

						// hack: CTRL+C handler
						if(ctrlc) {
							ctrlc = false;
							Clipboard.SetText(t_get(args[index], Math.Min(cpos, cpose), Math.Max(cpos, cpose)));
						}
					}

					ConsoleKeyInfo c = Console.ReadKey(true);
					switch(c.Key) {
						case ConsoleKey.Escape:
							return;     // exit prg

						case ConsoleKey.Enter:
							goto end;   // accept vars

						case ConsoleKey.F1:
							pause ^= true;
							goto message;   // pause (no/yes)

						case ConsoleKey.F2:
							debug ^= true;
							goto message;   // debug (no/yes)

						case ConsoleKey.UpArrow:
							Console.SetCursorPosition(32, 0 + index);
							Console.Write(args[index]);

							index--;
							if(index < 0)
								index = 2;

							cpos = args[index].Length;
							cpose = cpos;
							goto repaint;   // go up 1 line

						case ConsoleKey.DownArrow:
							Console.SetCursorPosition(32, 0 + index);
							Console.Write(args[index]);

							index++;
							index %= 3;

							cpos = args[index].Length;
							cpose = cpos;
							goto repaint;   // go down 1 line

						case ConsoleKey.LeftArrow:
							if(cpos > 0) {
								cpos--;
								if((c.Modifiers & ConsoleModifiers.Shift) == 0) {
									cpose = cpos;
								}
							}
							goto repaint;   // go up 1 line

						case ConsoleKey.RightArrow:
							if(cpos < args[index].Length) {
								cpos++;
								if((c.Modifiers & ConsoleModifiers.Shift) == 0) {
									cpose = cpos;
								}
							}
							goto repaint;   // go up 1 line

						case ConsoleKey.Delete:
							args[index] = "";
							cpos = 0;
							cpose = 0;
							goto repaint;   // del all text

						case ConsoleKey.Backspace:
							if(cpos != cpose) {
								args[index] = t_rmv(args[index], Math.Min(cpos, cpose), Math.Max(cpos, cpose));
							} else if(cpos > 0) {   // remove char
								args[index] = t_rmv(args[index], cpos - 1, cpos);
								//	args[index] = args[index].Substring(0, cpos - 1) + args[index].Substring(cpos, args[index].Length - cpos);
								cpos--;
							}

							if(cpos > args[index].Length)
								cpos = args[index].Length;

							cpose = cpos;
							goto repaint;

						default:
							if((c.Modifiers & ConsoleModifiers.Control) != 0) {
								if(c.Key == ConsoleKey.V) {
									if(Clipboard.ContainsText()) {
										if(cpos != cpose) {
											args[index] = t_rmv(args[index], Math.Min(cpos, cpose), Math.Max(cpos, cpose));

											if(cpos > args[index].Length)
												cpos = args[index].Length;
										}

										string t = Clipboard.GetText(TextDataFormat.Text);
										args[index] = t_put(args[index], cpos, t);
										cpos += t.Length;
										cpose = cpos;
									}
									goto repaint;

								} else if(c.Key == ConsoleKey.X) {
									if(cpos != cpose) {
										Clipboard.SetText(t_get(args[index], Math.Min(cpos, cpose), Math.Max(cpos, cpose)));
										args[index] = t_rmv(args[index], Math.Min(cpos, cpose), Math.Max(cpos, cpose));

										if(cpos > args[index].Length)
											cpos = args[index].Length;

										cpose = cpos;
										goto repaint;
									}
									goto loop;

								} else if(c.Key == ConsoleKey.A) {
									cpose = 0;
									cpos = args[index].Length;
									goto loop;
								}
							}

							if(cpos != cpose) {
								args[index] = t_rmv(args[index], Math.Min(cpos, cpose), Math.Max(cpos, cpose));

								if(cpos > args[index].Length)
									cpos = args[index].Length;
							}

							args[index] = t_put(args[index], cpos, "" + c.KeyChar);
							cpos++;
							cpose = cpos;
							goto repaint;
					}

				repaint:
					Console.SetCursorPosition(32, 0 + index);
					Console.Write(sp);
					Console.SetCursorPosition(32, 0 + index);
					t_cct(args[index], 0, Math.Min(cpos, cpose), ConsoleColor.White, ConsoleColor.Black);
					t_cct(args[index], Math.Min(cpos, cpose), Math.Max(cpos, cpose), ConsoleColor.Black, ConsoleColor.White);
					t_cct(args[index], Math.Max(cpos, cpose), args[index].Length, ConsoleColor.White, ConsoleColor.Black);
					Console.SetCursorPosition(Math.Min(32 + cpos, Console.BufferWidth - 1), 0 + index);
					goto loop;

				end:
					sp = "";
					for(int i = 1;i < Console.BufferWidth;i++)
						sp += ' ';

					Console.ForegroundColor = ConsoleColor.Gray;
					Console.BackgroundColor = ConsoleColor.Black;
					Console.SetCursorPosition(0, 6);

					Console.WriteLine(sp);
					Console.WriteLine(sp);
					Console.WriteLine(sp);
					Console.WriteLine(sp);
					Console.WriteLine(sp);
					Console.WriteLine(sp);
					Console.SetCursorPosition(0, 6);
				}

				// 3 or more commandline argumets, get arguments from the commandline
				if(args.Length >= 3) {
					while(args[0].StartsWith("-")) {
						switch(args[0].ElementAt(1)) {
							case 'd':
								debug = !debug;
								break;

							default:
								System.Console.WriteLine("Warn: Flag '-" + args[0].ElementAt(1) + "' does not exist!");
								break;
						}

						// remove first argument.
						args = args.Skip(1).ToArray();
					}

					fin = folder + "\\music\\" + args[0];

					if(args[1].Contains('.')) {
						settings = folder + "\\SMPS\\" + args[1].Split('.')[0] + "\\smps2asm script." + args[1].Split('.')[1] + ".asm";

					} else {
						settings = folder + "\\SMPS\\" + args[1] + "\\smps2asm script.asm";
					}

					name = args[2];

					// 1 or 2 arguments, show usage to user
				} else {
					Console.WriteLine("Illegal number of arguments!");
					error("Usage: smps2asm <filename.extension> <driver folder> <project name>");
					return;
				}

				// removes the extension of input file and adds .asm as the extension of output file
				if(fin.IndexOf(".", fin.LastIndexOf("\\")) > 0) {
					fout = fin.Substring(0, fin.LastIndexOf(".")) + ".asm";

				} else {
					fout = fin + ".asm";
				}

				// makes sure necessary files exist
				if(!File.Exists(settings)) {
					if(cmd) {
						error("File '" + settings + "' does not exist!");
					}

					message = "File '" + settings + "' does not exist!";
					goto fail;
				}

				if(!File.Exists(fin)) {
					if(cmd) {
						error("File '" + fin + "' does not exist!");
					}

					message = "File '" + fin + "' does not exist!";
					goto fail;
				}
			}

			// parse settings file
			try {
				string z = "";
				foreach(string s in args) {
					z += ", " + s;
				}

				dt("Arguments in current run '"+ z.Substring(2) +"'");
				dt("Parsing script at '"+ settings + "'");
				Console.WriteLine("Parsing script...");
				parseSettings(File.ReadAllText(settings).Replace("\t", "").Replace("\r", ""), args.Skip(3).ToArray());
			} catch (Exception e) {
				error("Could not parse script: ", e);
			}
			
			// start new timer
			timer = new Stopwatch();
			timer.Start();
			// create some other objects
			lables = new List<OffsetString>();
			lines = new List<OffsetString>();

			// open input file for reading
			try {
				Equate off = ((Dic)sett.GetValue("dat")).GetValue("offset") as Equate;
				if (off != null) {
					if(!off.calculated) off.calc();
					offset = (uint)off.value;
					dt("Data offset set to '"+ toHexString(offset, 4) +"'");
				}
				
				// read all bytes from input file
				dat = File.ReadAllBytes(fin);
				// create array of bools which determines if byte is skipped or not.
				skippedBytes = new bool[dat.Length];
				// Now, translate the file into data lines of code.
				dt("Input file size is " + dat.Length +" ("+ toHexString(dat.Length, 8) + ")");
				TranslateFile();

			} catch (Exception e) {
				error("Could not read input file: ", e);
			}
			
			// write info about how long it took to translate file
			timer.Stop();
			long tra = timer.ElapsedMilliseconds;
			Console.WriteLine("Input File translated! Took "+ tra +" ms!");
			dt("Translation took "+ tra +" ms");

			// restart timer
			timer.Reset();
			timer.Start();

			// here, we write out the file, check for unused bytes, and create lables.
			try {
				if (!File.Exists(fout)) {
					File.Create(fout).Close();
				}

				Console.WriteLine("Writing out the file...");
				dt("File write started");

				// The old header hack has been removed. Apparently changes later has removed
				// issue and -1 address requirement! Therefore, we can now leave the definition
				// up to the script file itself!
				string o = "", currLine = "";
				uint currLineArg = 0, nxtchk = 0;
				bool lastwaslable = true;

				// now starts the main loop which will search for all the lables
				for (int x = (int) offset - 1;x < offset + dat.Length;x++) {
					bool linechanged = false;
					// fake line to be replaced by current line when done. Used to also check if 2 different lines overlap
					OffsetString ln = new OffsetString(ulong.MaxValue, 1, ">>>>>>>>>>>>>>>");

					foreach (OffsetString o1 in lines) {
						// check if this line should go here
						if ((int) o1.offset == x && !ln.line.Equals(o1.line)) {
							// checks if there is another line found already. Warns the user if so
							if (ln.length > 0 && (ulong)x == ln.offset) {
								Console.WriteLine("Warning: Could not decide line at " + toHexString(x, 4) + "! '" + ln.line + "' vs '" + o1.line + "'");
								dt("Conflict at "+ toHexString(x, 4) + " '"+ ln.line + "' vs '"+ o1.line + "'");
							}

							ln = o1;
							linechanged = true;
							// check if this is data
							if (o1.line.StartsWith("db ")) {
								dt("Data at "+ toHexString(x, 4) +", arg# "+ currLineArg +"', data "+ o1.line.Substring(3));
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
									dt("Split data with "+ currLineArg +" bytes");
									o += currLine.Substring(0, currLine.Length - 2) + "\r\n";
									currLineArg = 0;
								}

								// add the line to file
								o += o1.line + "\r\n";
								lastwaslable = false;
								dt(o1.line);
							}
						}
					}

					// checks if no line was found for this byte,
					// last line did not have its bytes extend here,
					// and this byte was not in skipped bytes list
					if (!linechanged && x >= nxtchk && (x - offset) >= 0 && !skippedBytes[x - offset]) {
						if (currLineArg == 0) {
							// if no bytes exist yet, note this as unused bytes and start line
							currLine = "\t; Unused\r\n\tdc.b ";

						} else if (currLineArg >= 8) {
							// if there are 8 bytes in line, split the line and start new one
							o += currLine.Substring(0, currLine.Length - 2) + "\r\n";
							currLineArg = 0;
							currLine = "\tdc.b ";
						}

						// add a byte to the line
						string z = toHexString(ReadByte((uint) (x - offset)), 2);
						dt("Unused at " + toHexString(x, 4) + ", arg# " + currLineArg + "', data " + z);
						currLine += z +", ";
						currLineArg++;
					}

					// check if any lables are placed at the offset
					foreach (OffsetString o1 in lables) {
						if ((int) o1.offset == x + 1) {
							// split bytes of there are any.
							if (currLineArg > 0) {
								dt("Split data with " + currLineArg + " bytes");
								o += currLine.Substring(0, currLine.Length - 2) + "\r\n";
								currLineArg = 0;
							}

							// checks if last line also was a lable. Adds extra line break if not
							if (lastwaslable) {
								o += o1.line + "\r\n";

							} else {
								o += "\r\n" + o1.line + "\r\n";
								lastwaslable = true;
							}

							dt("Lable at " + toHexString((int)o1.offset, 4) +"', name " + o1.line);
						}
					}

					// if line was found, do not check for unused bytes until the line's bytecount is up
					if (linechanged && ln.length > 0) {
						nxtchk = (uint)((uint)x + ln.length);
						dt("Next unused check at "+ toHexString((int) nxtchk, 4));
					}
				}

				// split byte line if there was some left
				if(currLineArg > 0) {
					dt("Split data with " + currLineArg + " bytes");
					o += currLine.Substring(0, currLine.Length - 2) + "\r\n";
				}

				// write this to the out file
				File.WriteAllText(fout, o);
				dt("File writeout successful");

			} catch (UnauthorizedAccessException) {
				error("Could not create output file: Insufficient permissions");

			} catch (IOException e) {
				error("Could not create output file: IOException; "+ e.Message);

			} catch (Exception e) {
				error("Could not create output file: ", e);
			}

			// write information about how long it took (debugging purposes)
			timer.Stop();
			if (debug) {
				dt("File writeout successful in " + timer.ElapsedMilliseconds + " ms");
				dt("Total time is " + (timer.ElapsedMilliseconds + tra) + " ms");
				wd();
			}

			Console.WriteLine("Wrote output file! Took " + timer.ElapsedMilliseconds + " ms!");
			Console.WriteLine("SMPS2ASM conversion successful! Took " + (timer.ElapsedMilliseconds + tra) + " ms!");

			if(pause) {
				Console.ReadKey();
			}
		}

		private static void TranslateFile() {
			dt("Starting file translation");
			// first parse header
			Console.WriteLine("Parsing header...");
			dt("Parsing header");
			parseAllFunctions2(getDic("header"));

			// setup the stuffs!
			inHeader = false;
			followlable = true;
			dt("Header parsed, parsing all found lables");

			// parse all of the lables we found in header
			foreach (OffsetString o in lables.ToArray()) {
				Console.WriteLine("Parsing " + o.line + "...");
				if (o.line.Contains("DAC")) {
					// if DAC code, use DAC variables and command flags
					start = boff = (uint) (o.offset - offset);
					dt("Parsing DAC data '"+ o.line + "' at "+ toHexString(boff, 4));
					parseInput(new Dic[] { getDic("comm"), getDic("DAC") });

				} else if (o.line.Contains("FM") || o.line.Contains("PSG")) {
					// if FM or PSG code, use note variables and command flags
					start = boff = (uint) (o.offset - offset);
					dt("Parsing "+ (o.line.Contains("FM") ? "FM" : "PSG") +" data '" + o.line + "' at " + toHexString(boff, 4));
					parseInput(new Dic[] { getDic("comm"), getDic("note") });

				} else if (o.line.Contains("Patches")) {
					// set current file offset, too
					uint x = start = boff = (uint) (o.offset - offset);
					dt("Parsing patch data '" + o.line + "' at " + toHexString((int) boff, 4));
					// parse all function in patch
					parseAllFunctions2(getDic("patch"));

					// mark all bytes as skipped
					for(;x < boff;x++) {
						skippedBytes[x] = true;
					}
				}
			}
		}

		private static Dic getDic(string v) {
			Dic d = sett.GetValue(v) as Dic;

			if(d == null)	error("Dictionary '"+ v +"' not found! Are you sure you defined it?");
			return d;
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
			//	Console.WriteLine("WARN: Checking functions after end of SMPS file! (size: " + dat.Length + ", offset: "+ boff +") Please report to Natsumi if this causes a crash!");
				//		error("Could not resolve file: Out of file bounds (size: " + dat.Length + ", offset: "+ boff +')');
				dt("WARN: Checking functions after end of file at " + toHexString((int) boff, 4));

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
			dt("Parsing functions at " + toHexString((int) boff, 4));
			foreach (KeyValuePair<string, object> kv in d.GetKeyset()) {
				parseFunction(kv.Key, kv.Value, d, false);
			}
		}

		private static void parseFunction(string key, object value, Dic parent, bool writeEqu) {
		//	Console.WriteLine(ReadByte(boff) + " " + boff);
			if (value.GetType() == typeof(Equate)) {
				// parse equate
				Equate v = (Equate) value;
				if (!v.calculated) {
					v.calc();
					dx('=' + key + " c " + v.value);
				}

				if (writeEqu && ReadByte(boff) == v.value) {
					PutOnLine(key);
					dx('=' + key + ' ' + v.value);
					boff++;
				}

			} else if (value.GetType() == typeof(EquateChange)) {
				// change equate value
				Equate e = FindEquate(((EquateChange) value).name, sett);
				if(e == null) {
					error("Could not resolve file: Could not find equate '"+ ((EquateChange) value).name + "'!");
				}

				// plop the new value in and calculate it!
				e.raw = ((EquateChange) value).value;
				e.calc();
				dx("==" + ((EquateChange) value).name + " " + e.value);

			} else if (value.GetType() == typeof(Condition)) {
				// condition
				Condition v = (Condition) value;
				dx("¤ " + TranslateAllAbstract(v.GetCondition()) +" {");
				if (parseBool(v.GetCondition(), -1)) {
					parseAllFunctions2(v.True);
					dx("}");

				} else {
					dx("} {");
					parseAllFunctions2(v.False);
					dx("}");
				}

			} else if (value.GetType() == typeof(Command)) {
				// we just need to emulate a translation, and then ignore the result!
				// easy right? Just remember to not use 'aw' or 'ow', that will screw things up!
				dx("$ " + ((Command) value).command);
				TranslateAllAbstract(((Command) value).command);

			} else if (value.GetType() == typeof(Repeat)) {
				// repeat block
				Repeat v = (Repeat) value;
				long t = parseLong(v.times, -1);
				dx("* " + t + " {");
				for (long i = 0; i < t;i++) {
					dx("*" + i);
					parseAllFunctions2(v);
				}

			} else if (value.GetType() == typeof(ChangeLable)) {
				// change lable format
				dx("~" + TranslateAllAbstract(((ChangeLable) value).lable));
				lblparent = TranslateAllAbstract(((ChangeLable) value).lable);

			} else if(value.GetType() == typeof(ArbLable)) {
				// arbitary lable
				uint v = (uint)parseLong(((ArbLable)value).off, -1);
				string o = pickLable(v);
				dx("^ " + v +' '+ o);
				AddLable(v, o + ':');

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
				
				dx("%" + ln);
				string abc = ln.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\r", "\r");
				if(specialcomment) {
					OffsetString os = lines.ElementAt(lines.Count - 1);
					if(!os.line.EndsWith("\t")) os.line += '\t';
					os.line += abc;

				} else AddLable(xoff + offset, '\t' + abc);

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

				dx("> " + boff);

			} else if (value.GetType() == typeof(Macro) && checkMacroArgs((Macro)value)) {
				// macro block
				Macro v = (Macro) value;
				uint xoff = boff;
				boff += (uint)v.requiredBytes();

				string arg = "";
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

				if (arg.Length > 2) {
					arg = arg.Substring(2);
				}

				string n = v.getName().Replace("\\t", "\t");
				OutLine((uint) (offset + xoff), boff - xoff, "\t" + n + "\t" + arg);

				if (debug) {
					string a = "";
					foreach(byte s in v.flags) {
						a += ", " + toHexString(s, 2);
					}
					
					if(a.Length > 2) {
						a = a.Substring(2);
					}

					dx("!" + a +" "+ n + " "+ arg +";");
				}

				specialcomment = true;
				parseAllFunctions(v);
				specialcomment = false;

			} else if (value.GetType() == typeof(ArgMod)) {
				// argument modifier
				ArgMod v = (ArgMod) value;
				OffsetString ln = lines.ElementAt(lines.Count - 1);
				int i = ln.line.LastIndexOf('\t') + 1;
				string[] args = ln.line.Substring(i, ln.line.Length - i).Split(',');

				if (v.ID < args.Length && v.ID >= 0) {
					string x = FindValue(parseLong(args[v.ID].Replace("$", "0x"), -1), v), y = args[v.ID];
					if (x != null && x.Length > 0) {
						if(v.ID != 0) x = " "+ x;
						args[v.ID] = x;

					} else {
						x = "null";
					}

					string o = ln.line.Substring(0, i);
					args[0].Replace(" ", "");
					foreach(string a in args) {
						o += a +",";
					}

					ln.line = o.Substring(0, o.Length - 1);
					dx("#"+ v.ID +" " + y + " " + x.Replace(" ", ""));

				} else {
					if (v.ID < 0) {
						error("Negative argument number; " + v.ID + "!");

					} else {
						error("Not enough arguments in target function; " + v.ID + " expected, " + args.Length + " found!");
					}
				}

			} else if (value.GetType() == typeof(Stop)) {
				dx(";");
				stop = true;
				return;

			} else if(value.GetType() == typeof(Import)) {
				if(importd < importdmax) {
					dt("Import tree is too deep. Max "+ importdmax +" imports.");
					error("Import subscript cycle too deep! Maximum of "+ importdmax +" deep import tree is allowed!");
				}

				Dic d = sett.GetValue((value as Import).name) as Dic;
				if(d == null) {
					dt("Subscript '"+ (value as Import).name + "' does not exist.");
					error("Failed to find imported subscript '"+ (value as Import).name + "'!");
				}

				importd++;	// add 1 to the import depth level. If is greather than 3, it will be flagged as infinite loop and denied.
				parseAllFunctions(d);
				importd--;
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
			if(boff + m.requiredBytes() > dat.Length) {
				return false;
			}

			byte[] bytes = new byte[m.requiredBytes()];
			for(int i = 0;i < bytes.Length;i++) {
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

		private static Equate FindEquate(string name, Dic d) {
			foreach (KeyValuePair<string, object> kv in d.GetKeyset()) {
				if (kv.Value.GetType() == typeof(Dic)) {
					Equate r = FindEquate(name, kv.Value as Dic);
					if (r != null) return r;

				} else if (name.Equals(kv.Key)) {
					if (!((Equate) kv.Value).calculated) ((Equate) kv.Value).calc();
					return((Equate) kv.Value);

				}  
			}

			return null;
		}

		private static string FindValue(long val, Dic d) {
			foreach (KeyValuePair<string, object> kv in d.GetKeyset()) {
				if(kv.Value.GetType() == typeof(Dic)) {
					string r = FindValue(val, kv.Value as Dic);
					if(r != null) return r;

				} else if(kv.Value.GetType() == typeof(Import)) {
					string name = (kv.Value as Import).name;
					Dic dd = sett.GetValue(name) as Dic;
					if(dd == null) {
						dt("Subscript '" + name + "' does not exist.");
						error("Failed to find imported subscript '" + name + "'!");
					}

					string r = FindValue(val, dd);
					if(r != null) return r;

				} else if(kv.Value.GetType() == typeof(Equate)) {
					if(!((Equate)kv.Value).calculated)
						((Equate)kv.Value).calc();

					if(val == ((Equate)kv.Value).value) {
						return kv.Key;
					}
				}
			}

			return null;
		}

		private static string TranslateAllAbstract(string s) {
			try {
				while (s.Contains(".")) {
					int i = s.IndexOf(".") + 1;
					string tr = TranslateAbstract(s.Substring(i, 2));
					s = s.Substring(0, i - 1) + tr + s.Substring(i + 2, s.Length - i - 2);
				}

				while (s.Contains("\\")) {
					int i = s.IndexOf("\\") + 1, o = s.IndexOf("\\", i);
					Equate tr = FindEquate(s.Substring(i, o - i), sett);

					if (tr == null) {
						error("Could not find equate '" + s.Substring(i, o - i) + "'");
					}

					if(!tr.calculated) tr.calc();
					s = s.Substring(0, i - 1) + tr.value + s.Substring(o + 1, s.Length - o - 1);
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
					return ReadWordOff(boff - 2, -1);

				case "rw":	// fucking Ristar piece of shit
					boff += 2;
					return ReadWordOff(boff - 2, 0);

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

				case "pc":
					return "" + boff;

				case "sz":
					return "" + dat.Length;

				case "of":
					return "" + offset;

				case "an": {
						ulong off = (uint)dat.Length + offset;

						foreach(OffsetString o in lables) {
							if(o.offset > boff && o.offset < off) {
								off = o.offset;
								break;
							}
						}
						return "" + off;
					}

				case "al": {
						ulong off = 0;

						foreach(OffsetString o in lables) {
							if(o.offset <= boff && o.offset >= 0) {
								off = o.offset;
								break;
							}
						}
						return "" + off;
					}
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
			ushort pos;

			if (endian.Equals("\"little\"")) {
				pos = (ushort) ((ReadByte(off) | (ReadByte(off + 1) << 8)));

			} else if (endian.Equals("\"big\"")) {
				pos = (ushort) (((ReadByte(off) << 8) | ReadByte(off + 1)));

			} else {
				error("Could not resolve endianness '" + endian.Replace("\"", "") + "'");
				return null;
			}
			
			string o = pickLable(pos);
			AddLable(pos, o +':');

			uint st = start;
			if (followlable && ((pos - offset > boff) || (pos - offset < start))) {
				uint coff = boff;
				start = boff = pos - offset;
				parseInput(currDics);
				boff = coff;
			}

			start = st;
			dt("Lable '" + o + "' from " + toHexString(boff + offset, 4) + " points to " + toHexString(pos, 4));
			return o;
		}

		private static string ReadWordOff(uint off, int mod) {
			string endian = ((Equate) ((Dic) sett.GetValue("dat")).GetValue("endian")).raw;
			ushort pos;
			string lable = lblparent;

			if (endian.Equals("\"little\"")) {
				pos = (ushort) (boff + (ReadByte(off) | (ReadByte(off + 1) << 8)) + mod);

			} else if (endian.Equals("\"big\"")) {
				pos = (ushort) (boff + ((ReadByte(off) << 8) | ReadByte(off + 1)) + mod);

			} else {
				error("Could not resolve endianness '" + endian.Replace("\"", "") + "'");
				return null;
			}

			string o = pickLable(pos);
			AddLable(pos, o +':');

			uint st = start;
			if (followlable && ((pos - offset > boff) || (pos - offset < start))) {
				uint coff = boff;
				start = boff = pos - offset;
				parseInput(currDics);
				boff = coff;
			}

			start = st;
			dt("Lable '" + o + "' from " + toHexString(boff + offset, 4) + " points to " + toHexString(pos, 4));
			return o;
		}

		private static string ReadWordHdr(uint off) {
			string endian = ((Equate) ((Dic) sett.GetValue("dat")).GetValue("endian")).raw;
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

			string o = pickLable(pos);
			AddLable(pos, o +':');

			uint st = start;
			if (followlable && ((pos - offset > boff) || (pos - offset < start))) {
				uint coff = boff;
				start = boff = pos;
				parseInput(currDics);
				boff = coff;
			}

			start = st;
			dt("Lable '" + o + "' from " + toHexString(boff + offset, 4) + " points to " + toHexString(pos, 4));
			return o;
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

		private static string pickLable(uint pos) {
			int x = 0;
			while (hasLable(lblparent.Replace("#", "" + (++x)) + ':', pos)) {
				if (!lblparent.Contains("#") || x > 100) {
					error("Infinite string substitution loop for lable '" + lblparent + "'. Additionally, repeat count was " + (x - 1));
					return null;
				}
			}

			return lblparent.Replace("#", "" + x);
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
			LinkedList<Condition> co = new LinkedList<Condition>();
			int tabc = 0;
			string tab = "";
			foreach(string line in data.Split('\n')) {
				lnum++; // keep count of line number
				if (debug) {
					tab = "";
					for(int i = 0;i < tabc;i++) {
						tab += '\t';
					}

					if(tab.Length == 0) tab = " ";
				}

				if (line.Length > 0) {
					switch (line.ElementAt(0)) {
						case '}':
							if (stack.Count < 1) {
								error("smps2asm script.asm:Line " + lnum + ": There is nothing to return to. Seems like this is an extra terminator");
							}

							stack.RemoveFirst();
							if (inCondition && isTrue && line.EndsWith("{")) {
								isTrue = !isTrue;
								stack.AddFirst(co.First.Value.False);

								if (debug) {
									tab = "";
									for (int i = 1;i < tabc;i++) {
										tab += '\t';
									}

									if (tab.Length == 0) tab = " ";
									d(lnum + ':'+ tab +"} {");
								}

							} else {
								// pop first from the condition stack
								if(co.Count > 0) co.RemoveFirst();
								if (debug) {
									tabc--;
									tab = "";

									for (int i = 0;i < tabc;i++) {
										tab += '\t';
									}

									if (tab.Length == 0) tab = " ";
									d(lnum + ':' + tab + '}');
								}
							}
							break;

						case '=':
							try {
								string name = line.Substring(1, line.IndexOf(' ') - 1), l = line.Substring(line.IndexOf(' ') + 1);
								if (FindEquate(name, stack.ElementAt(stack.Count - 2)) != null) {
									// HACK WARNING: Create an element to change the value of pre-existing equate.
									// had to do this so no element tries to double-define itself and cause crash.
									// yes it is very hacky but it also works, so fuck off.
									stack.First.Value.Add("EquChange "+ lnum, new EquateChange(name, l));
									d(lnum + ':'+ tab + "==" + name + ' ' + l);

								} else {
									// attempt to add an item to the Dictionary
									stack.First.Value.Add(name, new Equate(l));
									d(lnum + ':'+ tab +'=' + name +' '+ l);
								}

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.First.Value + "'! ", e);
							}
							break;

						case '?':
							if(line.EndsWith(" {")) {
								Dic d1 = new Dic(new Dictionary<string, object>());
								try {
									string name = line.Substring(1, line.IndexOf(' ') - 1);
									stack.First.Value.Add(name, d1);
									stack.AddFirst(d1);
									d(lnum + ':' + tab + '?' + name + " {");
									tabc++;

								} catch(Exception e) {
									error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.First.Value + "'! ", e);
								}
							} else if(line.EndsWith(";")) {
								try {
									string name = line.Substring(1, line.IndexOf(';') - 1);
									stack.First.Value.Add(name, new Import(name));
									d(lnum + ':' + tab + '?' + name + ';');

								} catch(Exception e) {
									error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.First.Value + "'! ", e);
								}

							} else {
								error("smps2asm script.asm:Line " + lnum + ": Illegal end of line!");
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
								stack.First.Value.Add(nam, d2);

								if (line.EndsWith("{")) {
									stack.AddFirst(d2);
									tabc++;
								}

								d(lnum + ':'+ tab + line);

							} catch(Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Could not parse line! ", e);
							}
							break;

						case '@':
							try {
								string lable = line.Substring(1, line.IndexOf(' ') - 1), ln;
								long num = parseLong(line.Substring(lable.Length + 2, line.IndexOf(' ', lable.Length + 2) - (lable.Length + 2)), lnum);
								int i = line.IndexOf('"') + 1;
								string ask = line.Substring(i, line.LastIndexOf('"') - i);

								if (args.Length <= num) {
									ln = GetInput(ask);

								} else {
									ln = args[num];
								}
							
								// attempt to add an item to the Dictionary
								stack.First.Value.Add(lable, new Equate(ln));
								d(lnum + ':'+ tab +'@' + lable +' '+ num +' '+ ask);
								d(lnum + ':'+ tab +'=' + lable + ' ' + ln);

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.First.Value + "'! ", e);
							}
							break;

						case '#':
							try {
								int id = Int32.Parse(line.Substring(1, line.Replace(" ", "").IndexOf('{')));
								ArgMod ar = new ArgMod(new Dictionary<string, object>(), id);
								// attempt to add an item to the Dictionary
								stack.First.Value.Add("ARG "+ id, ar);
								stack.AddFirst(ar);
								d(lnum + ':'+ tab +'#' + id +" {");
								tabc++;

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.First.Value + "'! ", e);
							}
							break;

						case '*':
							try {
								Repeat re = new Repeat(line.Substring(2, line.LastIndexOf(' ') - 2), new Dictionary<string, object>());
								// attempt to add an item to the Dictionary
								stack.First.Value.Add("REPT " + lnum, re);
								stack.AddFirst(re);
								d(lnum + ':'+ tab + line);
								tabc++;

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.First.Value + "'! ", e);
							}
							break;

						case '$':
							try {
								Command comm = new Command(line.Substring(2));
								// attempt to add an item to the Dictionary
								stack.ElementAt(0).Add("COMM " + lnum, comm);
								d(lnum + ':'+ tab + line);

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.First.Value + "'! ", e);
							}
							break;

						case '~':
							try {
								ChangeLable comm = new ChangeLable(line.Substring(1).Replace("£", name));
								// attempt to add an item to the Dictionary
								stack.First.Value.Add("LABL " + lnum, comm);
								d(lnum + ':'+ tab +'~' + comm.lable);

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.First.Value + "'! ", e);
							}
							break;

						case ';':
							try {
								// attempt to add an item to the Dictionary
								stack.ElementAt(0).Add("STOP " + lnum, new Stop());
								d(lnum + ':'+ tab +';');

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.First.Value + "'! ", e);
							}
							break;

						case '¤':
							try {
								co.AddFirst(new Condition(line.Substring(2, line.LastIndexOf(' ') - 2).Replace(" ", ""),
									new Dictionary<string, object>(), new Dictionary<string, object>()));
								// attempt to add an item to the Dictionary
								stack.First.Value.Add("CONDITION " + lnum, co.First.Value);
								stack.AddFirst(co.First.Value.True);
								isTrue = inCondition = true;
								d(lnum + ':'+ tab + line);
								tabc++;

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.First.Value + "'! ", e);
							}
							break;

						case '%':
							try {
								stack.First.Value.Add("COMMENT " + lnum, new Comment(line.Substring(1)));
								d(lnum + ':'+ tab + line);

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.First.Value + "'! ", e);
							}
							break;

						case '>':
							try {
								switch (line.ElementAt(1)) {
									case 'a':case 'f':case 'b':
										break;

									default:
										// something has gone terribly wrong
										error("smps2asm script.asm:Line " + lnum + ": Go to type '"+ line.ElementAt(1) +"' not recognized!");
										break;
								}

								stack.First.Value.Add("GOTO " + lnum, new Goto(line.ElementAt(1), line.Substring(3)));
								d(lnum + ':'+ tab + line);

							} catch (Exception e) {
								error("smps2asm script.asm:Line " + lnum + ": Can not add an item to Dictionary '" + stack.First.Value + "'! ", e);
							}
							break;

						case '^':
							stack.First.Value.Add("LABLE " + lnum, new ArbLable(line.Substring(2)));
							break;

						default:
							error("smps2asm script.asm:Line " + lnum + ": Symbol not recognized: '"+ line.ElementAt(0) +"'");
							return;
					}
				}
			}

			if(stack.Count > 1) {
				dt(lnum + ": Not terminated!");
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
				try {
					if (s.Contains("!")) {
						type = s.ElementAt(0);
						len = Int32.Parse(s.Substring(1, s.IndexOf('!') - 1));
						s = s.Substring(s.IndexOf('!') + 1);
					}
				} catch (Exception) {
					// if it failed, we then did not want any type afterall or it was bad type.
					// easiest thing is just to ignore it.
					type = '\0';
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
			Console.Write(str +": ");
			string ret;

			do {
				ret = Console.ReadLine();
				if(ret.Length < 1) {
					Console.Write("Length must not be under 1 characters! ");
				}

			} while (ret.Length < 1);
			return ret;
		}

		// print error info, and then exits the program after user input is received
		public static void error(string str) {
			wd();
			Console.Write(str);
			Console.ReadKey(true);
			Environment.Exit(-1);
		}

		public static void error(string str, Exception e) {
			wd();
			Console.Write(str);
			Console.WriteLine(e.ToString());
			Console.ReadKey(true);
			Environment.Exit(-1);
		}

		// prints debug INFO level text
		private static void dt(string v) {
			if (debug) d("--- " + v + " ---");
		}

		// prints debug NORMAL level text
		private static void d(string v) {
			if (debug) defl += v + "\r\n";
		}

		// prints debug NORMAL level text with hex offset
		private static void dx(string v) {
			if(debug) d(toHexString(boff, 4) + " " + v);
		}

		private static void wd() {
			if (debug) File.WriteAllText(fout + ".log", defl);
		}
	}
}
