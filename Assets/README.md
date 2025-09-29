# Supercent Assignment – Bakery Loop (WebGL)

> **제출 메모**  
> 이번 빌드는 애니메이션/사운드/UI 연출을 의도적으로 **최소화**하고, 실험에 필요한 **핵심 루프/경제/언락/좌석/청소/지갑 차감**을 우선 구현했습니다.  
> 브라우저(WebGL) 전제에 맞춰 **풀링/경량화/노쓰레드**로 구성했습니다.

---

## TL;DR
- **핵심 루프:** 생성 → 픽업 → POS(공유 큐) → (Takeout: 즉시 결제 / Dine-in: 좌석 선점 후 테이블) → 식사 → 쓰레기 → 청소 → 다음 손님 → 언락 결제(실제 지갑 차감).
- **우선 순위:** 연출보다 **정확한 게임플로우 + 튜닝성 + 데이터화**.
- **주요 구현:**
  - POS 공유 큐(테이크아웃/다인인 분리 세팅)
  - 다인인: **좌석 해금+가용** 시 계산대에서 좌석 선점 → 테이블 이동 → 식사 종료 시 **돈 스택 + 쓰레기 생성**
  - 쓰레기 존재 시 **좌석 차단**(dirty) → 플레이어 근접 시 **자동 청소** → 다음 손님 진행
  - 언락 결제 시 **PlayerWallet에서 실제 차감**(`SpendUpTo`)
  - 풀링/경량화(DOTween kill, 코루틴 wait 캐싱, LINQ 미사용)

---

## Why (의도 & 제약)
- Supercent의 **데이터 기반 반복 실험** 문화에 맞춰, 시스템을 **모듈화**하고 핵심 변수는 인스펙터/ScriptableObject로 **노출**.
- **시간 제약**으로 연출은 최소화했으며, 대신 **경제/큐/좌석/언락/청소** 같은 코어 구조를 견고하게 구현.
- **WebGL 제약**(쓰레드 X, 파일 I/O 지양, 메모리 제한)에 맞춰 **오브젝트 풀링**/**코루틴 기반**으로 설계.

---

## Core Loop (상세)
1. **생산**: `ProductGenerator`가 `GeneratorConfigSO` 기반으로 아이템 생성 → `ProductShelf`에 재고 동기화.
2. **픽업**: `CustomerAgent`가 원하는 `ProductType`/수량을 선반에서 픽업(부족 시 선반 대기).
3. **POS 대기열**: `CheckoutLane`에서 대기 → 플레이어가 `CheckoutCounter` 트리거에 들어오면 맨 앞 손님 처리.
  - **Takeout**: 계산대에서 즉시 결제 → `SalesTracker` 집계 → `MoneyStacker`에 돈 스택.
  - **Dine-in**: **테이블 구역 해금 + 좌석 가용**이면, 계산대에서 **좌석 선점**만 하고 짧은 지연 후 `AfterCheckout()` 호출로 **테이블 이동**(계산대에서는 돈 스택 X).
4. **테이블 배치**: 손님은 테이블에 **보유 아이템 전량**을 그리드로 **적층**(y 오프셋/층간 높이 노출값 사용).
5. **식사 종료**:
  - 테이블 위 아이템 **Despawn**(풀 회수)
  - **쓰레기 프리팹** 생성(좌석 상태를 **dirty**로 전환)
  - **돈 스택 생성**: 씬에 배치된 **다인인 전용 MoneyStacker**에 `StackAmount(revenue)`
  - `SalesTracker`에 판매 집계
6. **청소 게이트**: 쓰레기 존재 시 해당 좌석은 **예약 불가**. 플레이어가 근접하면 `TableTrash`가 **자동 청소** → 좌석 **clean**으로 전환 → **다음 손님 진행**.
7. **언락 결제**: `UnlockZone`에서 플레이어가 지불 → `PlayerWallet.SpendUpTo(step)`으로 **실제 잔액만큼** 진행 → 완납 시 `UnlockableArea.Unlock()`.

---

## Key Systems

### POS & Queue
- `CheckoutLane` / `CheckoutCounter`
  - **프리팹 분리 세팅**:
    - `CheckoutCounter_Takeout`: 기존 결제/돈 스택/연출.
    - `CheckoutCounter_DineIn`: `isDineInCounter=On`, 좌석 해금+가용 시에만 진행, 순간 지연 후 **콜백에서 `who.AfterCheckout()` 명시 호출**로 테이블 이동 보장.
  - 다인인 카운터는 계산대에서 **돈 스택을 쌓지 않음**(테이블에서 정산).

### 좌석/테이블 & 청소 게이트
- `TableSeatManager`
  - 좌석 **예약/해제**, **가용 조회**, **다인인 가격표** 제공.
  - 좌석 구조에 `dirty`/`trash` 추가: **쓰레기 존재 시 예약 차단**.
  - `OnSeatFreed`(옵션)로 좌석 클린 시 카운터 재평가 트리거 가능.
- `CustomerAgent`
  - 테이블 도착 시 아이템 **전량 적층**(`tableCols/Rows/CellSize`, `tableYOffset`, `tableLayerHeight`로 튜닝).
  - 식사 종료 시 **Despawn → 쓰레기 생성 → 돈 스택 → 집계**.
  - 좌석/스택/단가는 **계산대/스포너에서 주입**받음(런타임 null 방지).
- `TableTrash`
  - 플레이어 근접 감지 → **자동 청소** → `TableSeatManager.MarkSeatClean()` 호출로 좌석 즉시 개방.

### 스폰 & 의존성 주입
- `CustomerSpawner`
  - 스폰 시점에 손님에게 필수 참조 **주입**:
    - `InjectTableSeats(TableSeatManager)`
    - 다인인일 때 `InjectDineInMoneyStacker(MoneyStacker)`
  - 주입 방식으로 씬 의존성을 **명시**하고, 런타임 null을 사전에 차단.

### 경제 & 언락
- `SalesTracker` : 판매(제품/수량/수익) 집계.
- `MoneyStacker` : 풀 기반 현금 오브젝트 스택(`PoolManager` 키 **"CashBill"**).
- `PlayerWallet` : `Add(amount)`, **`SpendUpTo(amount)`**(가능한 만큼만 지출) 제공.
- `UnlockZone` : 결제 루프에서 **`wallet.SpendUpTo(step)`** 사용 → **잔액만큼만** 진행, 완납 시 `UnlockableArea.Unlock()` 실행. 오버차지/마이너스 방지.

### 경량 이벤트 로깅
- 필요 시 `Analytics.Log(evt, payload)`(한 줄 JSON, 세션/시간 포함)로 KPI 포인트 로깅.
  ```json
  {"t":12.35,"evt":"checkout_start","data":{"counter":"DineIn"}}
  {"t":23.10,"evt":"dinein_seat_reserved","data":{"seat":"S02"}}
  {"t":42.88,"evt":"unlock_pay","data":{"spent":10,"paid":60,"remain":40}}

## Data & Tuning

### ScriptableObject
- **ProductType** : `id`, `displayName`, `icon`
- **GeneratorConfigSO** : `spawnInterval`, `batch`, `startDelay`
- **(확장 여지)** PriceTableSO *(현재는 카운터/좌석 매니저 내부 테이블 사용)*

### Inspector 핵심 값
- **CheckoutCounter**
  - `isDineInCounter`, `lane`, `serviceSeconds`, `moneyStacker` *(null 허용)*, `tableSeats`, `tableAreaUnlock`
- **CustomerAgent**
  - **Table Placement (Dine-In)**: `tableCols/Rows/CellSize`, `tableYOffset` *(상판 높이)*, `tableLayerHeight` *(층 간 높이)*
  - **Dining – Table Drop/Trash/Cash**: `trashLocalOffset` *(y로 쓰레기 높이 조절)*, *(선택)* `tableCommonYRaise`
- **TableSeatManager**
  - 좌석 리스트(`seatAnchor`, `tablePlace`, *(선택)* 좌석별 `MoneyStacker`)
  - `priceTable` *(다인인 단가)*
- **UnlockZone**
  - `totalCost`, `valuePerBill`, `perBillStagger`, `playerLayers`, `targetArea`

---

## Scene Setup Checklist

### PoolManager
- 키 `"CashBill"` 등록 + 지폐 프리팹에 `PooledObject.poolKey = "CashBill"`.

### Counters
- **CheckoutCounter_Takeout**: `lane=TakeoutLane`, `moneyStacker` 연결.
- **CheckoutCounter_DineIn**: `isDineInCounter=On`, `lane=DineInLane`, `tableSeats/areaUnlock/dineInMoneyStacker` 연결, 콜백에서 `who.AfterCheckout()` 호출.

### TableSeatManager
- 좌석 리스트에 `seatAnchor`/`tablePlace`/(선택) 좌석별 `MoneyStacker` 지정.
- 다인인 `priceTable` 입력.

### CustomerSpawner
- 스폰 시 `InjectTableSeats(...)`, 다인인일 때 `InjectDineInMoneyStacker(...)` 주입.

### CustomerAgent
- 테이블 배치 높이(`tableYOffset`), 층간 높이(`tableLayerHeight`), 쓰레기 높이(`trashLocalOffset.y`) 튜닝.

### UnlockZone
- 지불 처리에서 `wallet.SpendUpTo(step)` 사용, 완납 시 `UnlockableArea.Unlock()` 연결.

---

## How to Test (빠른 시나리오)
1. 손님 스폰 → 진열대에서 픽업 *(부족 시 선반 대기)*.
2. 플레이어가 POS 진입 →
  - **Takeout**: 계산대에서 결제/돈 스택 확인.
  - **Dine-in**: 해금+좌석 가용 시 **좌석 선점** → 테이블 이동.
3. 테이블 도착 → 아이템 **전량 적층** *(필요 시 `tableYOffset`/`tableLayerHeight` 조절)*.
4. 식사 종료 → 테이블 위 **Despawn**, **쓰레기 생성**, **돈 스택 생성**, **판매 집계**.
5. 쓰레기 근접 → **자동 청소** → 좌석 **clean** → 다음 손님 진행.
6. 언락 존에서 결제 → **PlayerWallet 감소** 확인 → 완납 시 영역 오픈.

---

## WebGL 고려
- **풀링** 중심, `DOTween.Kill` 정리, **코루틴 Wait 캐시**, **LINQ 미사용**으로 GC 스파이크 최소화.
- 파일 I/O 대신 **콘솔/메모리 로깅** *(필요 시 Export 버튼만 추가)*.
- 충돌 필터(레이어마스크)로 Overlap 최소화. **쓰레드 사용 X**.

---

## Known Limitations & Next Steps
- **연출 최소화**: 테이블 배치/청소/언락 연출은 다음 단계에서 강화.
- **UI/피드백 간소화**: 진행 바/좌석 상태/언락 표시 보강 예정.
- **세이브/리커버리 미포함**: WebGL 세션 기준. `PlayerPrefs`/간단 직렬화로 확장 가능.
- **밸런스/A-B**: 생산속도/대기열 우선순위/가격은 인스펙터/데이터로 즉시 조정 가능.

---

## Troubleshooting (자주 틀리는 부분)

### 다인인 돈이 안 쌓임
- 스포너에서 `InjectDineInMoneyStacker` 주입 여부 확인.
- **단가 0?** → 계산대에서 `GetUnitPrice`로 전달 또는 `TableSeatManager.priceTable` 확인.
- `PoolManager` `"CashBill"` 키/프리팹/`PooledObject.poolKey` 일치 확인.

### 다음 손님이 테이블로 안 감
- 계산대 다인인 콜백에서 `who.AfterCheckout()` **명시 호출** 확인.
- 좌석이 `dirty`면 예약 불가 → `TableTrash` 청소 되었는지 확인.
- `TableSeatManager` 참조가 **스포너 주입**으로 들어갔는지 확인.

### 언락 돈이 마이너스
- 언락 루프에서 `wallet.Add(-pay)` 대신 `wallet.SpendUpTo(step)` 사용.

---

## Appendix – Class Map

### Flow
`CustomerSpawner` → `CustomerAgent`(주입) → `ProductShelf` 픽업 → `CheckoutLane/Counter` →  
(Takeout: `SalesTracker` + `MoneyStacker`) / (Dine-in: `TableSeatManager` 좌석) →  
테이블 배치/식사/쓰레기 → 청소 → 다음 손님

### Economy
`SalesTracker`, `MoneyStacker`, `PlayerWallet`, `UnlockZone`, `UnlockableArea`

### Logistics
`ProductGenerator`, `ProductShelf`, `AutoItemTransfer`, `ProductCarrier`, `StackCarrier`

### Infra
`PoolManager`, `PooledObject`, *(옵션)* `Analytics`

