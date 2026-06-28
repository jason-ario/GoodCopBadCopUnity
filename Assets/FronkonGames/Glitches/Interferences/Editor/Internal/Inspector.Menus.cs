////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) Martin Bustos @FronkonGames <fronkongames@gmail.com>. All rights reserved.
//
// THIS FILE CAN NOT BE HOSTED IN PUBLIC REPOSITORIES.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering;

namespace FronkonGames.Glitches.Interferences.Editor
{
  /// <summary> Support window. </summary>
  public abstract partial class Inspector : VolumeComponentEditor
  {
    [MenuItem("Help/Fronkon Games/Glitches/Interferences/Online documentation")]
    public static void OnlineDocumentation() => Application.OpenURL(Constants.Support.Documentation);

    [MenuItem("Help/Fronkon Games/Glitches/Interferences/Leave a review ❤️")]
    public static void LeaveAReview() => Application.OpenURL(Constants.Support.Store);
 }
}