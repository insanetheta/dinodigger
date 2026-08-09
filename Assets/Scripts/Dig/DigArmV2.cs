using UnityEngine;
using DinoDigger.Config;

namespace DinoDigger.Dig
{
    /// <summary>
    /// Dig-arm V2 (DinoDigger-rrn): the proportionate slim excavator-arm ART SET,
    /// selected by <see cref="GameConfig.DigArmVersion"/> (default V1) and
    /// live-switchable mid-dig via the editor menu "DinoDigger/Demo/Dig Arm V2 On|Off".
    ///
    /// STRICTLY ART-ONLY by design: the rig skeleton (ArmPivot/Boom/Elbow/Stick/
    /// Wrist/Bucket), bone lengths, IK, joint limits, body staging and the whole
    /// bite state machine are V1's, untouched — V2 only remounts which sprites hang
    /// on the bones, through the very same pin-to-pin mounting (AssignSegmentPins /
    /// AssignBucket) V1 uses. That is what makes the switch safe to flip at any
    /// moment, even mid-bite: joint angles and targets never change, so bites keep
    /// resolving and TestArmReady semantics hold in both positions.
    ///
    /// WHY V2 EXISTS (Greg): the V1 art reads GIANT next to the vehicle. Measured
    /// from the shipped sprites: the V1 boom is drawn 1.80 world units deep over its
    /// 3.4-unit pin span (a slab, 1:1.9), its base pin boss is ~1.65 units across
    /// (bigger than a whole dirt tile), and the elbow mates a small round tip boss
    /// to a big hexagonal plate. The whole vehicle is only 2.4 units tall, so the
    /// arm out-masses its own machine. V2's art is drawn 1:6-8 slender with small
    /// SAME-diameter bosses at the elbow (clean concentric knuckle) and a bigger
    /// 1.0-unit bucket, so the mass hierarchy reads body > bucket > arm.
    ///
    /// Pin constants below were MEASURED from the generated art by
    /// Tools/generate_digarm2.py `measure` (dark pin-hole blob centroids; the same
    /// numbers land in Assets/Art/Generated/digarm2/pins.json and in
    /// GeneratedArtImporter's V2 block — re-run measure + sync all three if the art
    /// is ever regenerated).
    /// </summary>
    public partial class DigModeController
    {
        // Normalized (0..1, bottom-left origin) pin boss centroids of the V2 art.
        private static readonly Vector2 V2BoomBasePin = new Vector2(0.1258f, 0.2487f);
        private static readonly Vector2 V2BoomTipPin = new Vector2(0.8959f, 0.7540f);
        private static readonly Vector2 V2StickBasePin = new Vector2(0.1166f, 0.5069f);
        private static readonly Vector2 V2StickTipPin = new Vector2(0.8824f, 0.5093f);

        // V2 bucket renders 1.0 units tall (V1: 0.72): on a toy digger the bucket is
        // the biggest mass after the body — never smaller than a joint knuckle.
        private const float V2BucketH = 1.0f;

        // Which art set is currently mounted on the rig. Purely observational —
        // the rig's behavior is identical either way.
        private bool _armV2Mounted;

        /// <summary>TEST HOOK. True while the V2 sprites are the ones mounted.</summary>
        internal bool TestArmV2Mounted => _armV2Mounted;

        /// <summary>TEST HOOK. True when every arm segment renderer is enabled with a
        /// sprite assigned — "the arm renders", whichever art set is mounted.</summary>
        internal bool TestArmRenders =>
            _boom != null && _boom.enabled && _boom.sprite != null &&
            _stick != null && _stick.enabled && _stick.sprite != null &&
            _bucket != null && _bucket.enabled && _bucket.sprite != null;

        /// <summary>TEST HOOK. True when the library carries the full V2 sprite set
        /// (i.e. DinoDigger/Import Generated Art ran since the digarm2 art landed).</summary>
        internal bool TestArmV2ArtAvailable =>
            _lib != null && _lib.Boom2Sprite != null && _lib.Stick2Sprite != null &&
            _lib.Bucket2Sprite != null;

        /// <summary>V2 is selected AND its full art set is importable. Missing V2
        /// sprites (library never re-imported) leave the rig safely on V1.</summary>
        private bool ArmV2Ready =>
            _config != null && _config.DigArmVersion == DigArmVersion.V2 &&
            _lib != null && _lib.Boom2Sprite != null && _lib.Stick2Sprite != null &&
            _lib.Bucket2Sprite != null;

        /// <summary>Called by PlaceBackhoe after the V1 rig is assembled: if the config
        /// selects V2 (and its art exists), remount the V2 sprites over the same bones.</summary>
        private void ApplyDigArmVersion()
        {
            if (ArmV2Ready)
            {
                MountArmV2();
            }
            else
            {
                _armV2Mounted = false; // V1 sprites are already mounted by PlaceBackhoe
            }
        }

        /// <summary>Live-switch entry point (editor demo menu + tests): remount the art
        /// set the config currently selects onto the live rig, mid-dig, without touching
        /// joint state. Safe to call any time; a no-op outside an open dig site.</summary>
        public void RefreshDigArmVersion()
        {
            if (!_open || _armPivot == null || _boom == null || _stick == null ||
                _bucket == null)
            {
                return;
            }

            if (ArmV2Ready)
            {
                MountArmV2();
            }
            else
            {
                MountArmV1();
            }
        }

        /// <summary>Mount the V2 set through the same pin-to-pin math as V1 (uniform
        /// scale, zero stretching; the drawn pin spacing lands exactly on the bone).</summary>
        private void MountArmV2()
        {
            AssignSegmentPins(_boom, _lib.Boom2Sprite, BoomLen, V2BoomBasePin, V2BoomTipPin);
            AssignSegmentPins(_stick, _lib.Stick2Sprite, StickLen, V2StickBasePin, V2StickTipPin);
            AssignBucket(_bucket, _lib.Bucket2Sprite, V2BucketH);
            _armV2Mounted = true;
        }

        /// <summary>Remount the V1 set — the exact mounting PlaceBackhoe performs, using
        /// V1's own constants, so toggling back mid-dig restores V1 pixel-for-pixel.
        /// (PlaceBackhoe itself is untouched; this exists only for the LIVE switch.)</summary>
        private void MountArmV1()
        {
            Sprite fallback = _lib != null ? _lib.ScoopArm : null;
            if (_lib != null && _lib.BoomSprite != null)
            {
                AssignSegmentPins(_boom, _lib.BoomSprite, BoomLen, BoomBasePin, BoomTipPin);
            }
            else
            {
                AssignSegmentFallback(_boom, fallback, BoomLen, BoomThick);
            }

            if (_lib != null && _lib.StickSprite != null)
            {
                AssignSegmentPins(_stick, _lib.StickSprite, StickLen, StickBasePin, StickTipPin);
            }
            else
            {
                AssignSegmentFallback(_stick, fallback, StickLen, StickThick);
            }

            AssignBucket(_bucket,
                _lib != null && _lib.BucketSprite != null ? _lib.BucketSprite : fallback,
                BucketH);
            _armV2Mounted = false;
        }
    }
}
