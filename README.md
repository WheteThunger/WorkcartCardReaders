## Features

- Adds keycard readers to workcarts (intended for automated workcarts)
- Requires players to swipe an acceptable keycard in order to ride
- Ejects players who do not swipe within a short period of time
- Can be configured to use vanilla keycards or keycards with custom skins
- All vanilla keycards work by default, but higher level cards can be used more times

## Use cases

#### Use case #1: Integrate with Automated Workcarts (default)

If you are using the [Automated Workcarts](https://umod.org/plugins/automated-workcarts) plugin to add NPC conductors to workcarts, this plugin can be used to add card readers to only those workcarts. This is achieved by setting the `AddToAutomatedWorkcarts` configuration option to `true` (default).

#### Use case #2: Custom automated workcarts

If you are using a custom plugin to automate workcarts, that plugin can either use this plugin's API to add card readers to individual workcarts, or if you are automating all workcarts, then you can simply set the `AddToAllWorkcarts` configuration option to `true` to make all workcarts receive card readers.

## Permissions

- `workcartcardreaders.freerides` -- Allows the player to board workcarts without having to swipe a keycard.

## Configuration

Default configuration:

```json
{
  "AddToAllWorkcarts": false,
  "AddToAutomatedWorkcarts": true,
  "AllowedSecondsToSwipeBeforeEject": 10.0,
  "EnableGlobalAuthorization": false,
  "AuthorizationGraceTimeSecondsOffWorkcart": 60.0,
  "CardReaderAccessLevel": 1,
  "AcceptedCards": [
    {
      "AccessLevel": 1,
      "PercentConditionLossPerSwipe": 20
    },
    {
      "AccessLevel": 2,
      "PercentConditionLossPerSwipe": 15
    },
    {
      "AccessLevel": 3,
      "PercentConditionLossPerSwipe": 10
    },
    {
      "Skin": 1988408422,
      "PercentConditionLossPerSwipe": 0
    }
  ],
  "CardReaderPositions": [
    {
      "Position": {
        "x": 0.1,
        "y": 1.4,
        "z": 1.8165
      },
      "RotationAngles": {
        "x": 0.0,
        "y": 180.0,
        "z": 0.0
      }
    }
  ]
}
```

- `AddToAllWorkcarts` (`true` or `false`) -- While `true`, all workcarts will receive card readers. This is only recommended if you are using a custom plugin to automate workcarts and you are automating all of them.
- `AddToAutomatedWorkcarts` (`true` or `false`) -- While `true`, only workcarts automated via the [Automated Workcarts](https://umod.org/plugins/automated-workcarts) plugin will receive card readers. This is the default behavior of the plugin.
- `AllowedSecondsToSwipeBeforeEject` -- Determines the amount of time an unauthorized player has to swipe an acceptable keycard before being ejected from the workcart.
- `EnableGlobalAuthorization` (`true` or `false`) -- While `true`, swiping an acceptable keycard on one workcart authorizes you to ride any workcart. While `false`, you must swipe an acceptable keycard for each workcart individually.
- `AuthorizationGraceTimeSecondsOffWorkcart` -- Determines the amount of time an authorized player may leave a workcart and board it again without having to swipe a keycard. While `EnableGlobalAuthorization` is `true`, the player may freely board any other workcart during this time.
- `RequiredCardSkin` -- Determines the skin ID that keycards must have in order to be accepted. While this value is `0`, only vanilla keycards will be accepted, and they must match the access level of the card reader. While this value is non-`0`, only the skin ID is checked, and the access level does not need to match.
- `CardReaderAccessLevel` (`1` = Green, `2` = Blue, `3` = Red) -- Determines the color of the card reader. This does **not** affect which keycards are accepted.
- `AcceptedCards` -- List of accepted keycards. If a keycard is swiped and matches a given card config in this list, then the player will be authorized and the card condition will be deducted accordingly.
  - `Skin` -- When this option is non-`0`, any card with this skin ID will match.
  - `AccessLevel` (`1` = Green, `2` = Blue, `3` = Red) -- When this value is non-`0`, and `Skin` is `0` or not specified, any card with this access level will match.
  - `PercentConditionLossPerSwipe` -- Determines the condition percentage that the keycard will lose when used, if it matches this card config. For example, setting this to `20` allows a brand new keycard to be used up to 5 times.
- `CardReaderPositions` -- Determines how many card readers will spawn on each workcart, as well as their position and rotation relative to the workcart. By default, each workcart has one card reader which is attached to the back of the driver cabin.

## Localization

```json
{
  "Error.NoPermission": "You don't have permission to do that.",
  "Error.CardNotAccepted": "Error: That card is not accepted here.",
  "Warning.SwipeRequired": "You have <color=#f30>{0}</color> seconds to swipe a workcart pass.",
  "Success.AuthorizedToWorkcart": "You are <color=#3f3>authorized</color> to ride this workcart.",
  "Success.AuthorizedToAllWorkcarts": "You are <color=#3f3>authorized</color> to ride all workcarts.",
  "Success.AlreadyAuthorized": "You are already <color=#3f3>authorized</color>.",
  "Info.StillAuthorized": "You are still authorized for <color=#fd4>{0}</color> seconds.",
  "Info.RemovedForNotSwiping": "You were removed from the workcart because you did not swipe a workcart pass in time."
}
```

## Known Issues

When a workcart moves, its keycard readers may disappear temporarily. This is a purely cosmetic issue, meaning players can still interact with the invisible keycard readers. This plugin mostly mitigates the issue by causing the keycard readers to reappear when the workcart stops, so this issue should not affect gameplay most of the time.

## Developer API

#### API_AddCardReader

```csharp
bool API_AddCardReader(TrainEngine workcart)
```

- Adds one or more card readers to the workcart based on the plugin configuration.
- Returns `true` if successful or if the workcart already had card readers, else `false` if a plugin blocked it with the `OnWorkcartCardReaderAdd` hook.

#### API_RemoveCardReader

```csharp
void API_RemoveCardReader(TrainEngine workcart)
```

- Removes the card readers from the workcart.

#### API_HasCardReader

```csharp
bool API_HasCardReader(TrainEngine workcart)
```

- Returns `true` if the workcart has one or more card readers spawned by this plugin, else returns `false`.

#### API_AuthorizePlayer

```csharp
bool API_AuthorizePlayer(TrainEngine workcart, BasePlayer player)
```

- Authorizes the player to the specified workcart.
- Returns `true` if successful, else `false` if the workcart did not have a card reader of if a plugin blocked authorization with the `OnAutomatedWorkcartPlayerAuthorize` hook.
- While the plugin is configured with `EnableGlobalAuthorization`, this API will authorize the player to all workcarts, not specifically the one provided.

#### API_DeauthorizePlayer

```csharp
bool API_DeauthorizePlayer(TrainEngine workcart, BasePlayer player)
```

- Deauthorizes the player from the specified workcart.
- Returns `true` if successful, else `false` if the workcart did not have a card reader or if a plugin blocked deauthorization with the `OnAutomatedWorkcartPlayerDeauthorize` hook.
- Note: The player may still be authorized to ride the workcart if they have the `workcartcardreaders.freerides` permission.

#### API_IsPlayerAuthorized

```csharp
bool API_IsPlayerAuthorized(TrainEngine workcart, BasePlayer player)
```

- Returns `true` if the player is authorized to ride the workcart, else `false`.

## Developer Hooks

#### OnWorkcartCardReaderAdd

```csharp
bool? OnWorkcartCardReaderAdd(TrainEngine workcart)
```

- Called when this plugin is about to add one or more card readers to a workcart
- Returning `false` will prevent card readers from being added
- Returning `null` will result in the default behavior

#### OnWorkcartPlayerAuthorize

```csharp
bool? OnWorkcartPlayerAuthorize(TrainEngine workcart, BasePlayer player)
```

- Called when a player is about to be authorized to a workcart
- Returning `false` will prevent the player from becoming authorized
- Returning `null` will result in the default behavior

#### OnWorkcartPlayerAuthorized

```csharp
void OnWorkcartPlayerAuthorized(TrainEngine workcart, BasePlayer player)
```

- Called after a player has been authorized to a workcart
- No return behavior

#### OnWorkcartPlayerDeauthorize

```csharp
bool? OnWorkcartPlayerDeauthorize(TrainEngine workcart, BasePlayer player)
```

- Called when a player is about to be deauthorized from a workcart
- Returning `false` will prevent the player from becoming deauthorized
- Returning `null` will result in the default behavior

#### OnWorkcartPlayerDeauthorized

```csharp
void OnWorkcartPlayerDeauthorized(TrainEngine workcart, BasePlayer player)
```

- Called after a player has been deauthorized from a workcart
- No return behavior

#### OnWorkcartPlayerEject

```csharp
bool? OnWorkcartPlayerEject(TrainEngine workcart, BasePlayer player)
```

- Called when a player is about to be ejected from a workcart
- Returning `false` will prevent the player from being ejected
- Returning `null` will result in the default behavior

#### OnWorkcartEjectPositionDetermine

```csharp
Vector3? OnWorkcartEjectPositionDetermine(TrainEngine workcart, BasePlayer player)
```

- Called when a player is about to be ejected from a workcart
- Returning a `Vector3` will override the eject position
- Returning `null` will result in the default behavior

#### OnWorkcartPlayerEjected

```csharp
void OnWorkcartPlayerEjected(TrainEngine workcart, BasePlayer player)
```

- Called after a player has been ejected from a workcart
- No return behavior
