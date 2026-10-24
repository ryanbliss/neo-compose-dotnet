---
"com.ryanbliss.neocompose": patch
---

Preserve aliases and strict ownership when inserting nested collections. Keep early local collection References attached through field binding and replacement, and read generic collection References with their closed target types. Retain live ordinary aliases after removal with fresh Session identities, and reclaim unescaped retained graphs after the complete write commits.
