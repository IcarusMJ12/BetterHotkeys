using Barotrauma;

namespace BetterHotkeys {
  partial class BetterHotkeys : ACsMod {
    public BetterHotkeys() {
#if CLIENT
      InitClient();
#endif
    }
    public override void Stop() {}
  }
}
