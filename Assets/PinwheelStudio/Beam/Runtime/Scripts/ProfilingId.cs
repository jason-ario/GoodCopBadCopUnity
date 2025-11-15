using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Beam
{
    internal static class ProfilingId
    {
        public const string SCATTERING_PASS = "Volumetric Lighting/Scattering";
        public const string ACCUMULATION_PASS = "Volumetric Lighting/Accumulation";
        public const string FILTER_PASS = "Volumetric Lighting/Filter";
        public const string FILTER_DOWNSAMPLE = "Blur Down Sample";
        public const string FILTER_COMBINE = "Combine";
        public const string INTEGRATION_PASS = "Volumetric Lighting Integration";
    }
}
