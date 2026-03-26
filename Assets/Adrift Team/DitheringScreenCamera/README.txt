How to use the Dithering Shader:

	1. Create a render texture and input in the Size values the resolution you want to be displayed in the screen (for example 640 wide and 360 height like in
	   the provided render texture named "DitheringRendering640x360") and make sure that the filter mode is set to Point if you want to make a pixel art game.
	   The last thing you have to do on the Render Texture is to change the Color Format to R16G16B16A16_UNORM if you dont want to have color banding.

	2. In your Scene, create a Raw Image on the Canvas and attach the desired Render Texture on the Texture value. Make sure the Raw Image fills the whole screen.
	   
	3. Then go to the Camera of your scene and attach the same Render Texture on the Output Texture value, inside the Output section of the Camera component.

	4. Create a second Camera that will display just the UI elements, and make it a child of the previous camera. Once you do that, go to the first camera 
	   Culling Mask and take away the UI element. On the second Camera you want to take off everything from the Culling Mask except the UI elements.

	5. Create a new material with the SG_DitheringScreen shader, attach the render texture we created before and put a "BayerMatrix" texture on the Dither Image
	   value, changing the dithering pattern depending on the bayer you attach. If you want to use a bayer matrix of your own make sure that the Wrap Mode is
	   set to Repeat and the Compression value on None.

	6. The last step is to attach this material to the Raw Image we created previously in the Canvas and it should show what the camera is looking at with the 
	   dithering effect.


In the package are provided a handful of prefab examples which are also on the Demo scene. To see the different effects on the Game window you need to have
activated inside the "CanvasDitheringExamples" the "DitheringScreen" object you want to see and leave the others inactive.