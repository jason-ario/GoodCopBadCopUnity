# Known Risks

## Dirty Worktree

- `Assets/_GoodCopBadCop/_Fonts/My_handwriting SDF.asset` was already modified before this AI framework was added.
- Agents must not revert, overwrite, or normalize that asset unless Alex explicitly asks.

## Serialized Unity Assets

- Scene, prefab, material, `.asset`, Timeline, and `.meta` files can produce large diffs.
- Always inspect serialized diffs carefully.
- Avoid broad automated rewrites of Unity assets.

## Vendor Assets

- The repo contains many top-level vendor/imported asset folders.
- Do not modify vendor assets unless the task explicitly targets them.

## Product Code Organization

- Product scripts currently appear to live mostly in `Assembly-CSharp`, with no product asmdef found in the initial survey.
- Adding asmdefs would be a large architectural change and should not happen opportunistically.

## Netcode Complexity

- Many systems use `NetworkBehaviour`, `ServerRpc`, and `ClientRpc`.
- Changes must account for host, server, client, ownership, and late-join paths.

## Recovery and Demo Content

- `Assets/_Recovery` contains many recovery scenes and should not be treated as canonical gameplay content.
- Imported demo scenes and sample scripts exist in vendor folders.

## Shallow Missing-Reference Signals

- Initial text search found missing-script-like markers in a project volume profile and missing-prefab text in UMotion assets.
- Verify in Unity before attempting repairs; some matches may be harmless serialized text or plugin-specific data.
