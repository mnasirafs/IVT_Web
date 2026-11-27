# Feature dictionary (FEATURE_VERSION f1)

Generated from `src/ivt_ml/features.py` (`FEATURES`). One shared pipeline (`build_features`) serves training and scoring.
AE group = autoencoder reconstruction group (FINANCIAL / MEASUREMENT / RECONCILIATION); `-` = not an autoencoder input.
Clf = logistic-regression input. MoveIT (G) features are model inputs only when `UseMoveitFeaturesForTraining = 1` (default 0: MoveIT is not point-in-time).
AE transform: log = signed log1p; ratio = clipped to [-10, 10]; z = clipped to [-20, 20]; then median imputation + missing indicator + robust scaling.


## A. Financial

| Feature | Business label | Kind | AE group | AE transform | Clf | Definition |
|---|---|---|---|---|---|---|
| `BuyNett` | Buy nett | numeric | FINANCIAL | log | yes | source value (parsed) |
| `SellNett` | Sell nett | numeric | FINANCIAL | log | yes | source value (parsed) |
| `BuyFreight` | Buy freight | numeric | FINANCIAL | log | yes | source value (parsed) |
| `SellFreight` | Sell freight | numeric | FINANCIAL | log | yes | source value (parsed) |
| `BuyOther` | Buy other charges | numeric | FINANCIAL | log | yes | source value (parsed) |
| `SellOther` | Sell other charges | numeric | FINANCIAL | log | yes | source value (parsed) |
| `BuyFuel` | Buy fuel surcharge | numeric | FINANCIAL | log | yes | source value (parsed) |
| `SellFuel` | Sell fuel surcharge | numeric | FINANCIAL | log | yes | source value (parsed) |
| `MarginAmount` | Margin (sell - buy) | numeric | FINANCIAL | log | yes | SellNett - BuyNett |
| `MarginRatio` | Margin as a share of sell | numeric | FINANCIAL | ratio | yes | MarginAmount / SellNett when SellNett > 0, else missing + MarginRatioUndefinedFlag |
| `NegativeMarginFlag` | Negative margin | flag | FINANCIAL | flag | yes | MarginAmount < 0 |
| `BuyExceedsSellFlag` | Buy exceeds sell | flag | FINANCIAL | flag | yes | BuyNett > SellNett |

## B. Charge composition

| Feature | Business label | Kind | AE group | AE transform | Clf | Definition |
|---|---|---|---|---|---|---|
| `BuyFuelRatio` | Buy fuel as a share of buy freight | numeric | FINANCIAL | ratio | yes | BuyFuel / BuyFreight when BuyFreight > 0 |
| `SellFuelRatio` | Sell fuel as a share of sell freight | numeric | FINANCIAL | ratio | yes | SellFuel / SellFreight when SellFreight > 0 |
| `FuelRatioDifference` | Buy vs sell fuel ratio difference | numeric | FINANCIAL | ratio | yes | BuyFuelRatio - SellFuelRatio |
| `BuyOtherRatio` | Buy other charges as a share of buy nett | numeric | FINANCIAL | ratio | yes | BuyOther / BuyNett when BuyNett > 0 |
| `SellOtherRatio` | Sell other charges as a share of sell nett | numeric | FINANCIAL | ratio | yes | SellOther / SellNett when SellNett > 0 |
| `AncillaryChargeCount` | Ancillary charge lines | numeric | RECONCILIATION | log | yes |  |
| `BuyLineCount` | Buy charge lines | numeric | RECONCILIATION | log | yes |  |
| `SellLineCount` | Sell charge lines | numeric | RECONCILIATION | log | yes |  |

## C. Physical

| Feature | Business label | Kind | AE group | AE transform | Clf | Definition |
|---|---|---|---|---|---|---|
| `Weight` | Billed weight (kg) | numeric | MEASUREMENT | log | yes | source value (parsed) |
| `ChargeableWeight` | Billed chargeable weight (kg) | numeric | MEASUREMENT | log | yes | source value (parsed) |
| `Cubic` | Billed cubic | numeric | MEASUREMENT | log | yes | source value (parsed) |
| `Pallet` | Pallets | numeric | MEASUREMENT | log | yes | source value (parsed) |
| `Quantity` | Quantity | numeric | MEASUREMENT | log | yes | source value (parsed) |
| `Hours` | Hours | numeric | MEASUREMENT | log | no | source value (parsed) |
| `Lift` | Lift | numeric | MEASUREMENT | log | no | source value (parsed) |
| `Space` | Space | numeric | MEASUREMENT | log | no | source value (parsed) |

## D. Unit costs and relationships

| Feature | Business label | Kind | AE group | AE transform | Clf | Definition |
|---|---|---|---|---|---|---|
| `BuyPerChargeableKg` | Buy per chargeable kg | numeric | MEASUREMENT | log | yes | BuyNett / ChargeableWeight when > 0 |
| `SellPerChargeableKg` | Sell per chargeable kg | numeric | MEASUREMENT | log | yes | SellNett / ChargeableWeight when > 0 |
| `BuyPerCubic` | Buy per cubic | numeric | MEASUREMENT | log | yes | BuyNett / Cubic when > 0 |
| `SellPerCubic` | Sell per cubic | numeric | MEASUREMENT | log | yes | SellNett / Cubic when > 0 |
| `ChargeableToActualWeightRatio` | Chargeable vs actual weight | numeric | MEASUREMENT | ratio | yes | ChargeableWeight / Weight when Weight > 0 |
| `WeightPerCubic` | Weight per cubic | numeric | MEASUREMENT | log | yes | Weight / Cubic when Cubic > 0 |
| `ChargePerPallet` | Sell per pallet | numeric | MEASUREMENT | log | yes | SellNett / Pallet when Pallet > 0 |

## E. Historical peers (point in time)

| Feature | Business label | Kind | AE group | AE transform | Clf | Definition |
|---|---|---|---|---|---|---|
| `BuyNettRobustZ` | Buy nett vs comparable shipments | numeric | FINANCIAL | z | yes | (BuyNett - peer median) / (1.4826 x MAD); MAD=0 -> (P90-P10)/2.5631; both 0 -> missing + ZeroSpreadFlag |
| `SellNettRobustZ` | Sell nett vs comparable shipments | numeric | FINANCIAL | z | yes | as BuyNettRobustZ for SellNett |
| `MarginRatioRobustZ` | Margin vs comparable shipments | numeric | FINANCIAL | z | yes | as BuyNettRobustZ for MarginRatio |
| `BuyFuelRatioRobustZ` | Fuel ratio vs comparable shipments | numeric | FINANCIAL | z | yes | as BuyNettRobustZ for BuyFuelRatio |
| `BuyPerChargeableKgRobustZ` | Unit cost vs comparable shipments | numeric | MEASUREMENT | z | yes | as BuyNettRobustZ for BuyPerChargeableKg |
| `PeerCount` | Comparable shipments | numeric | - | - | yes | observations in the chosen peer group (strictly earlier weeks) |
| `PeerFallbackLevel` | Peer group generality | numeric | - | - | yes | 1 (Customer|Carrier|Lane|WorkCode) .. 5 (CarrierGroup); first level with >= MinimumPeerCount |
| `SparsePeerFlag` | Few comparable shipments | flag | RECONCILIATION | flag | yes | no level reached MinimumPeerCount |

## F. Buy/sell reconciliation

| Feature | Business label | Kind | AE group | AE transform | Clf | Definition |
|---|---|---|---|---|---|---|
| `MissingBuyFlag` | No buy charge | flag | RECONCILIATION | flag | yes | no buy line for the job in the week |
| `MissingSellFlag` | No sell charge | flag | RECONCILIATION | flag | yes | no sell line for the job in the week |
| `BuySellWeightDifference` | Buy vs sell weight difference (kg) | numeric | RECONCILIATION | log | yes | buy weight - sell weight |
| `BuySellChargeableWeightDifference` | Buy vs sell chargeable weight difference (kg) | numeric | RECONCILIATION | log | yes | buy chargeable weight - sell chargeable weight |
| `BuySellCubicDifference` | Buy vs sell cubic difference | numeric | RECONCILIATION | log | yes | buy cubic - sell cubic |
| `ChargeLineMismatchFlag` | Different number of buy and sell lines | flag | RECONCILIATION | flag | yes | buy and sell line counts differ (both > 0) |
| `MeasurementConflictFlag` | Charge lines disagree on measurements | flag | RECONCILIATION | flag | yes | charge lines disagree on a measurement |

## G. Dispatch reconciliation (MoveIT)

| Feature | Business label | Kind | AE group | AE transform | Clf | Definition |
|---|---|---|---|---|---|---|
| `BilledVsMoveitWeightRatio` | Billed vs dispatch weight | numeric | RECONCILIATION | ratio | yes | Weight / MoveIT TotWeightKg |
| `BilledVsMoveitCubicRatio` | Billed vs dispatch cubic | numeric | RECONCILIATION | ratio | yes | Cubic / MoveIT TotVolM |
| `BillingVsPickupPostcodeMismatch` | Pickup postcode differs from dispatch | flag | RECONCILIATION | flag | yes |  |
| `BillingVsDeliveryPostcodeMismatch` | Delivery postcode differs from dispatch | flag | RECONCILIATION | flag | yes |  |
| `DispatchToBillingDays` | Days from dispatch to billing week | numeric | - | - | yes | SnapshotWeek - MoveIT ActualDespatchDate |

## H. Rarity / context

| Feature | Business label | Kind | AE group | AE transform | Clf | Definition |
|---|---|---|---|---|---|---|
| `CustomerGroup` | Customer group | categorical | - | - | yes |  |
| `CarrierGroup` | Carrier group | categorical | - | - | yes |  |
| `WorkCode` | Work code | categorical | - | - | yes |  |
| `PreviouslyUnseenCombinationFlag` | New customer/carrier/service/lane combination | flag | RECONCILIATION | flag | yes | Customer|Carrier|WorkCode|Lane not seen in the lookback window |
| `HistoricalCombinationCount` | Times this combination was seen | numeric | - | - | yes | times seen in the lookback window |
| `ConsignmentAgeDays` | Days from consignment to billing week | numeric | - | - | yes | SnapshotWeek - consignment date |

## I. Availability

| Feature | Business label | Kind | AE group | AE transform | Clf | Definition |
|---|---|---|---|---|---|---|
| `MissingWeightFlag` | Missing weight | flag | RECONCILIATION | flag | yes |  |
| `MissingCubicFlag` | Missing cubic | flag | RECONCILIATION | flag | yes |  |
| `MissingMoveitFlag` | No dispatch record | flag | - | - | yes |  |
| `UnknownCurrencyFlag` | Currency unknown | flag | - | - | no | no approved ML.CurrencyMap entry |
| `InvalidDateFlag` | Invalid consignment date | flag | - | - | yes |  |
| `MarginRatioUndefinedFlag` | Margin ratio undefined (sell <= 0) | flag | FINANCIAL | flag | yes |  |
| `FuelRatioUndefinedFlag` | Fuel ratio undefined (freight <= 0) | flag | - | - | yes | BuyFreight <= 0 or SellFreight <= 0 |

## Autoencoder-derived classifier inputs (out of sample only)

`AEAnomalyPercentile`, log of `AEReconstructionError`, and log of the Financial/Measurement/Reconciliation group errors. For training they come from chronological expanding-fold autoencoders (`out_of_sample_ae_scores`), never from an autoencoder fitted on the same rows.

## Excluded inputs

Never used as model inputs (account-manager and contact fields are kept for routing only):

`AccountManager`, `ActionTime`, `ContactEmail`, `DeliveryAddress1`, `ErrCode`, `ErrDesc`, `HeldBy`, `LastModifiedTime`, `LoginName`, `Moveit_SpecialIns`, `PickupAddress1`, `Reason`, `ReviewOutcome`, `Sub_Reason`, `del_com`, review outcome fields, later modification/action information, free-text delivery instructions, personal names and full addresses.

Excluded pending business definitions: `space` and `lift` (kept as audit columns, not classifier inputs), `unit`, `rate_no`, `Moveit_RateIncSurch`, `Moveit_VolWeightKg`. `CurrentWeek` is used only as the snapshot week, never as a predictive value.
