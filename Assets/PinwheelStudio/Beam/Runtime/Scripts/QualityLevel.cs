using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Beam
{
    /// <summary>
    /// Specify the quality level for volumetric lighting and fog effect
    /// </summary>
    public enum QualityLevel
    {
        /// <summary>
        /// Dedicated to be used in game, lowest quality
        /// </summary>
        InGameLow, 
        /// <summary>
        /// Dedicated to be used in game, balance between quality and performance 
        /// </summary>
        InGame, 
        /// <summary>
        /// Dedicated to be used in high end PC or pre-recorded video.
        /// </summary>
        Cinematic, 
        /// <summary>
        /// Dedicated to be used in pre-recorded video, use lots of VRAM, slow to render.
        /// </summary>
        CinematicHigh
    }
}
