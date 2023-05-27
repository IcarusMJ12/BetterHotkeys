using Barotrauma;

namespace AlwaysEquipOnQuickUseAction {
  partial class AlwaysEquipOnQuickUseAction : ACsMod {
    public AlwaysEquipOnQuickUseAction() {
#if CLIENT
      InitClient();
#endif
    }
    public override void Stop() {}
  }
}
