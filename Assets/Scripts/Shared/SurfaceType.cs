using UnityEngine;

namespace CubeArena.Shared
{
    // Tags floor geometry with its noise-model surface category (docs/GAME_DESIGN.md
    // section 5: rug/cloth = silent, tile/wood = loud). The noise model itself isn't
    // built yet (Milestone 5) — this exists so KitchenBuilder's geometry only needs
    // tagging once, not revisited when Milestone 5 actually reads it (e.g. via a
    // downward raycast from each player's feet each tick).
    public enum Surface
    {
        Tile,
        Rug,
    }

    public class SurfaceType : MonoBehaviour
    {
        public Surface Surface;
    }
}
