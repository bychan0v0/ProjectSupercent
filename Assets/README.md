# Supercent Assignment – Bakery Loop (WebGL)

## Why (의도)
- 데이터 기반의 빠른 반복 실험을 위해 핵심 루프(생성→픽업→결제→보상→언락)를
  모듈화/이벤트화 하였습니다.
- 모든 KPI 포인트(픽업/품절/결제/보상/언락/진행)에서 한 줄 JSON 로그를 남깁니다.

## How (구조)
- Data
    - ProductType (SO), GeneratorConfigSO (SO), (선택) PriceTableSO (SO)
- Core Systems
    - ProductGenerator(IProductSource) → ProductShelf(IProductSource/Sink)
    - AutoItemTransfer(IProductCarrier) – StackCarrier(비주얼)
    - CheckoutLane/Counter – MoneyStacker – PlayerWallet
    - CustomerAgent – ShelfWaitingArea – TableSeatManager
- Analytics
    - `Analytics.Log(evt, payload)`로 모든 이벤트를 **한 줄 JSON**으로 기록
    - 예: `sale`, `pickup_one`, `shelf_wait_restock`, `checkout_start/done`, `cash_stack/collect`, `milestone`…

## Web Considerations
- 코루틴 캐시, 풀링, DOTween KILL, 레이어 필터, 무쓰레드.
- 로그는 `Debug.Log` + in-memory buffer만 사용(파일 I/O 없음).

## Experiments (A/B)
- PriceTableSO로 단가 테이블 교체
- GeneratorConfigSO로 생산 속도/배치 변경
- CheckoutLane spacing/serviceRadius 조정
- Customer restock polling interval 조정

## Export
- (선택) 간단 UI 버튼에서 `GUIUtility.systemCopyBuffer = Analytics.Dump(true);`
  → 브라우저 콘솔/개발자툴에서도 로그 수집 가능
