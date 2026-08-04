# Unequip Rule immutable version repair

The canonical `UnequipItemOnInteractionIntent` payload had changed while still
using definition version 2, so World Authority correctly rejected development
package publication during world entry. The revised payload is now immutable
version 3. Development publication, current weapon defaults, and reusable Rule
Block presets all reference version 3; historical migration records retain the
version that was true when they were authored.
