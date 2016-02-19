namespace smps2asm {
	internal class Equate {
		public bool calculated { get; set; }
		public long value { get; set; }
		public string raw { get; set; }

		public Equate(string v) {
			raw = v;
		}

		public void calc() {
			value = Program.parseLong(raw, -1);
			calculated = true;
		}
	}
}