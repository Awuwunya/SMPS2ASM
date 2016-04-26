namespace smps2asm {
	internal class Goto {
		public char dir { get; }
		public string off { get; }

		public Goto(char dir, string off) {
			this.dir = dir;
			this.off = off;
		}
	}
}