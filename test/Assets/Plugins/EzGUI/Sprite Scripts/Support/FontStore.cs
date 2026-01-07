//-----------------------------------------------------------------
//  Copyright 2010 Brady Wright and Above and Beyond Software
//	All rights reserved
//-----------------------------------------------------------------

using UnityEngine;
using System.Collections;
using System.Collections.Generic;


/// <remarks>
/// This class serves as a scene-wide store of all
/// fonts currently in use.  This is so we can
/// cache the font data once instead of having to
/// read from disk every time we create some text.
/// </remarks>
public static class FontStore
{
	// The list of fonts currently loaded.
    static Dictionary<TextAsset, SpriteFont> fontStore = new Dictionary<TextAsset, SpriteFont>();

	/// <summary>
	/// Returns the SpriteFont object for the
	/// specified definition file.
	/// If no existing object is found, it is
	/// loaded from storage.
	/// </summary>
	/// <param name="fontDef">The TextAsset that defines the font.</param>
	/// <returns>A reference to the font definition object.</returns>
	public static SpriteFont GetFont(TextAsset fontDef)
	{
		if (fontDef == null)
			return null;

		if (fontStore.ContainsKey(fontDef))
        {
            return fontStore[fontDef];
        }
        else
        {
            SpriteFont f = new SpriteFont(fontDef);
            fontStore.Add(fontDef, f);
            return f;
        }

	}

}