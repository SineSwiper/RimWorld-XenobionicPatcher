# About

This patcher mod looks through most of the surgery operations, and matches your modded surgeries (like new bionics) with your modded races and animals.  In other words, if you have a bionic expansion mod like [FSF's Vanilla Bionics Expansion](https://steamcommunity.com/sharedfiles/filedetails/?id=1419675146), [EPOE](https://steamcommunity.com/sharedfiles/filedetails/?id=725956940), or [RBSE](https://steamcommunity.com/sharedfiles/filedetails/?id=850429707), and a set of modded races or animals, this patcher will match them together a few different ways (examples in quotes):

* If the surgery works against a "Leg", and your pawn has a "Leg", your pawn gets the operation option.
* If the surgery works against a different named part, but your pawn definition really calls it a "Leg" in the label, the body part will be mapped together and your pawn will get other "Leg" operations.
* If the surgery is called "Install bionic arm", and your pawn also has a surgery called "Install bionic arm", the body part will be mapped together (like "Arm" -> "Tentacle") and your pawn will get other "Arm" operations.

On top of the surgery-to-race compatibilities, Xenobionic Patcher will also link humanlike operations with animals and visa-versa.  Think of the possibilities:

* Do your animals need bionic legs or an Orion Exoskeleton from [Glitter Tech](https://steamcommunity.com/sharedfiles/filedetails/?id=725576127)?  You got them!
* Is your best colonist quickly dying from a shot to the heart (only alive because of [Death Rattle](https://steamcommunity.com/sharedfiles/filedetails/?id=1552452572)) and the only other heart you own is some bionic animal heart you bought cheap from a trader?  You can save him!
* Do you want those sweet [Genetic Rim](https://steamcommunity.com/sharedfiles/filedetails/?id=1113137502) animal implants for yourselves?  Install them freely!

This patcher does not create any items or pawns. It merely links already known operations to other races and animals.

With this mod installed, you don't need any additional race or animal patchers. However, they shouldn't interfere with Xenobionic Patcher, even if you do.

Requires HugsLib.

# Features

* Searches for missing surgeries among various pawn types and adds them
* Eliminates the need for individual mod-to-mod race/surgery patchers or animal/surgery patchers
* Sorts surgeries to make them easier to find
* Compatible with most surgery mods, including [FSF's Vanilla Bionics Expansion](https://steamcommunity.com/sharedfiles/filedetails/?id=1419675146), [EPOE](https://steamcommunity.com/sharedfiles/filedetails/?id=725956940), [RBSE](https://steamcommunity.com/sharedfiles/filedetails/?id=850429707), [MSE](https://steamcommunity.com/sharedfiles/filedetails/?id=1749027802), [Cyber Fauna](https://steamcommunity.com/sharedfiles/filedetails/?id=1548649032), [A Dog Said...](https://steamcommunity.com/sharedfiles/filedetails/?id=746425621), etc.
* Customizable in the mod configuration screen

# Load Order
**Put this dead last** for best compatibility!  After any race mods, animal mods, bionic mods, medical mods, surgery mods, etc.

