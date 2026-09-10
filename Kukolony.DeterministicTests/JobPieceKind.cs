namespace Kukolony.Jobs;
// Mirrors the persisted enum codes. This project deliberately links the real validator.
internal enum JobPieceKind { Start, StopAtStockLimit, FindLooseItem, SelectSource, SelectTarget, MoveToTarget, PickUp, TakeItem, PutItem, OperateStation, End }
