namespace smps2asm {
	public class OffsetString {
		public ulong offset { get; }
		public ulong length { get; }
		public string line { get; set; }

		public OffsetString(ulong off, ulong len, string s) {
			offset = off;
			line = s;
			length = len;
		}
	}
}