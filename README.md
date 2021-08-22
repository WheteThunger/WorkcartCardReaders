## Features

- Adds keycard readers to automated workcarts
- Players must swipe an acceptable keycard in order to ride
- Ejects players who do not swipe within a short period of time
- Can be configured to use vanilla keycards or keycards with custom skins

## Required Plugins

- [Automated Workcarts](https://umod.org/plugins/automated-workcarts) -- Card readers are added only to workcarts that are automated via that plugin.

## Permissions

- `workcarttickets.freerides` -- Allows the player to get free rides from automated workcarts without swiping a keycard.

## Configuration

Default configuration:

```json
{
  "AllowedSecondsToSwipeBeforeEject": 10.0,
  "EnableGlobalAuthorization": false,
  "AuthorizationGraceTimeSecondsOffWorkcart": 60.0,
  "CardPercentConditionLossPerSwipe": 25,
  "RequiredCardSkin": 0,
  "CardReaderAccessLevel": 1,
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

- `AllowedSecondsToSwipeBeforeEject` -- Determines the amount of time an unauthorized player has to swipe an acceptable keycard before being ejected from the workcart.
- `EnableGlobalAuthorization` -- While `true`, swiping an acceptable keycard on one workcart authorizes you to ride any workcart. While `false`, you must swipe an acceptable keycard for each workcart individually.
- `AuthorizationGraceTimeSecondsOffWorkcart` -- Determines the amount of time an authorized player may leave a workcart and board it again without having to swipe a keycard. While `EnableGlobalAuthorization` is `true`, the player may freely board any other workcart during this time.
- `CardPercentConditionLossPerSwipe` (`0` - `100`) -- Determines the condition percentage that a keycard will lose when used. For example, setting this to `25` allows a brand new keycard to be used up to 4 times.
- `RequiredCardSkin` -- Determines the skin ID that keycards must have in order to be accepted. While this value is `0`, only vanilla keycards will be accepted, and they must match the access level of the card reader. While this value is non-`0`, only the skin ID is checked, and the access level does not need to match.
- `CardReaderAccessLevel` (`1` = Green, `2` = Blue, `3` = Red) -- Determines the color of the card reader. This also determines the level of keycard required if `RequiredCardSkin` is `0`.
- `CardReaderPositions` -- This list determines how many card readers will spawn on each automated workcart, as well as their position and rotation relative to the workcart.

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

The keycard readers may disappear when the workcarts move. This is a purely cosmetic issue, meaning players can still interact with them while they are invisible. This plugin mostly mitigates the issue by causing the keycard readers to re-appear when the workcart stops, so this issue should not affect gameplay most of the time.

## Developer API

#### API_HasCardReader

```csharp
bool API_HasCardReader(TrainEngine workcart)
```

- Returns `true` if the workcart has one or more card readers spawned by this plugin, else returns `false`.

#### API_IsPlayerAuthorized

```csharp
bool API_IsPlayerAuthorized(TrainEngine workcart, BasePlayer player)
```

- Returns `true` if the player is authorized to ride the workcart, else `false`.

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
- Note: The player may still be authorized to ride the workcart if they have the `workcarttickets.freerides` permission.

## Developer Hooks

#### OnWorkcartCardReaderAdd

```csharp
bool? OnWorkcartCardReaderAdd(TrainEngine workcart)
```

- Called when this plugin is about to add one or more card readers to an automated workcart
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
