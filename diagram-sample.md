# Mermaid 화려한 종합 테스트

Mermaid 공식 예제의 스타일, 분기, 주석, 다중 레이아웃, 도형, 연결선을 반영한 종합 샘플입니다.
모든 다이어그램은 외부 서버 없이 내장 Mermaid 렌더러로 표시됩니다.

## Flowchart — UART 진단 파이프라인

```mermaid
flowchart LR
    subgraph MCU["MCU Firmware"]
        BOOT(["Boot"]) --> INIT{{"UART Init"}}
        INIT --> LOOP[["Main Loop"]]
        LOOP --> RX[/"RX Frame"/]
        RX --> VALID{"Valid Frame?"}
        VALID -->|Yes| CMD["Decode Command"]
        VALID -->|No| ERR[("Error Counter")]
        CMD --> CORE[["Kts Dbg Core"]]
    end
    subgraph PC["PC Diagnostic Console"]
        PORT[("COM Port")] --> VIEW[["ANSI Dashboard"]]
        VIEW --> LOG[("Session Log")]
    end
    CORE -->|UART response| PORT
    PORT -->|KTS DBG| CORE
    CORE --> VIEW
    classDef firmware fill:#dbeafe,stroke:#2563eb,stroke-width:2px,color:#172554
    classDef transport fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#14532d
    classDef diagnostic fill:#fef3c7,stroke:#d97706,stroke-width:2px,color:#78350f
    class BOOT,INIT,LOOP,CMD,CORE firmware
    class RX,PORT,ERR transport
    class VALID,VIEW,LOG diagnostic
    style MCU fill:#eff6ff,stroke:#60a5fa,stroke-width:2px
    style PC fill:#fffbeb,stroke:#f59e0b,stroke-width:2px
```

## Sequence diagram — 실시간 ANSI 대시보드

```mermaid
sequenceDiagram
    autonumber
    actor Dev as Developer
    participant PC as Kts_DiagMon
    participant UART as MCU UART
    participant Core as Kts_DbgCore
    participant App as Firmware
    Dev->>PC: Open COM port
    PC->>UART: KTS DBG ON
    UART->>Core: Receive command frame
    Core->>App: Dispatch diagnostic command
    App-->>Core: Status and counters
    Core-->>UART: ANSI frame
    UART-->>PC: Stream dashboard update
    rect rgb(232, 245, 255)
        loop Every 1 ms
            PC->>UART: Poll status
            UART-->>PC: RX and TX counters
        end
    end
    alt Invalid command
        Core-->>UART: Error response
        UART-->>PC: Show warning panel
    else Valid command
        PC->>PC: Refresh full-screen ANSI view
    end
```

## Class diagram — 진단 모듈 구조

```mermaid
classDiagram
    direction LR
    class Kts_DbgCore {
        +Init()
        +Process()
        +HandleCommand(frame)
        +RenderAnsi()
    }
    class Kts_Uart {
        <<interface>>
        +Init(baudrate)
        +Read(buffer)
        +Write(buffer)
    }
    class RingBuffer {
        -uint8_t data[256]
        -size_t head
        -size_t tail
        +Push(byte)
        +Pop()
        +Available()
    }
    class CommandRouter {
        +Register(command)
        +Dispatch(frame)
    }
    class AnsiDashboard {
        +Clear()
        +DrawHeader()
        +DrawMetrics()
        +Flush()
    }
    class LogStore {
        +Append(event)
        +ExportCsv()
    }
    Kts_DbgCore *-- CommandRouter : owns
    Kts_DbgCore *-- AnsiDashboard : owns
    Kts_DbgCore o-- Kts_Uart : uses
    Kts_Uart *-- RingBuffer : buffers
    CommandRouter --> LogStore : records
    AnsiDashboard --> LogStore : reads
```

## State diagram — 진단 모드 상태

```mermaid
stateDiagram-v2
    direction LR
    [*] --> Booting
    Booting --> Protected : default safe mode
    Protected --> Ready : DBG ON
    Ready --> Streaming : CH ALL
    Ready --> Ready : CH MAIN
    Streaming --> AnsiMode : ANSI ON
    AnsiMode --> Streaming : ANSI OFF
    Streaming --> Ready : CH OFF
    Ready --> Protected : DBG OFF
    Protected --> [*] : shutdown
    state AnsiMode {
        [*] --> DrawFrame
        DrawFrame --> FlushFrame
        FlushFrame --> DrawFrame : next tick
    }
    note right of Streaming
        UART log frames are
        refreshed continuously
    end note
```

## Entity relationship diagram — UART 이벤트 모델

```mermaid
erDiagram
    MCU ||--|| UART_PORT : owns
    UART_PORT ||--o{ UART_FRAME : receives
    UART_FRAME ||--o| DIAG_COMMAND : contains
    DIAG_COMMAND ||--o{ DIAG_EVENT : produces
    DIAG_EVENT }o--|| ANSI_VIEW : updates
    DIAG_EVENT }o--|| SESSION_LOG : persists
    UART_PORT {
        string port_name
        int baudrate
        string mode
    }
    UART_FRAME {
        string timestamp
        string direction
        string payload
        int checksum
    }
    DIAG_COMMAND {
        string name
        string channel
        string argument
    }
    DIAG_EVENT {
        string severity
        string message
    }
```

## Gantt chart — 포팅 및 검증 일정

```mermaid
gantt
    title MCU UART 포팅 릴리스 로드맵
    dateFormat YYYY-MM-DD
    axisFormat %m/%d
    section Prepare
        Vendor sample audit       :done, audit, 2026-01-05, 3d
        UART pin and baud mapping :done, map, after audit, 4d
        Buffer sizing             :active, buffer, after map, 3d
    section Integrate
        Kts Uart adapter          :adapter, after buffer, 5d
        Command router            :router, after adapter, 4d
        ANSI dashboard            :ansi, after router, 5d
    section Verify
        Loopback test             :test, after ansi, 3d
        COM soak test             :soak, after test, 4d
        Release candidate         :milestone, rc, after soak, 0d
```

## Pie chart — UART 트래픽 구성

```mermaid
pie showData
    title UART 진단 세션 트래픽
    "TX command" : 25
    "RX response" : 38
    "ANSI refresh" : 27
    "Error and retry" : 10
```

## User journey — 개발자 검증 여정

```mermaid
journey
    title Kts DiagMon 포팅 검증 여정
    section Connect
        Select COM port: 5: Developer
        Open serial link: 5: Developer
        Confirm baudrate: 4: Developer
    section Diagnose
        Enable debug mode: 5: Developer
        Select channel: 4: Developer
        Inspect live counters: 5: Developer
    section Operate
        Open ANSI dashboard: 5: Developer
        Capture session log: 4: Developer
        Export test evidence: 5: Developer
```

## Git graph — 기능 브랜치 통합

```mermaid
gitGraph
    commit id: "vendor sample"
    branch feature/uart
    checkout feature/uart
    commit id: "map uart api"
    commit id: "add ring buffer"
    checkout main
    branch feature/ansi
    checkout feature/ansi
    commit id: "ansi dashboard"
    checkout main
    merge feature/uart tag: "uart-ready"
    merge feature/ansi tag: "diag-v1"
    commit id: "release"
```

## Requirement diagram — 제품 요구사항 추적

```mermaid
requirementDiagram
    requirement uart_req {
        id: 1
        text: "UART 송수신을 지원해야 한다"
        risk: High
        verifymethod: Test
    }
    requirement ansi_req {
        id: 2
        text: "ANSI 모드는 전체 화면을 갱신해야 한다"
        risk: Medium
        verifymethod: Demonstration
    }
    requirement log_req {
        id: 3
        text: "진단 이벤트를 세션 로그로 저장해야 한다"
        risk: Low
        verifymethod: Inspection
    }
    element mcu {
        type: firmware
        docref: "MCU UART implementation"
    }
    element pc {
        type: diagnostic_tool
        docref: "PC ANSI console"
    }
    element tester {
        type: verification
        docref: "Porting test suite"
    }
    uart_req - satisfies -> mcu
    ansi_req - satisfies -> pc
    log_req - verifies -> tester
```

## Mindmap — 시스템 구성 한눈에 보기

```mermaid
mindmap
  root((Kts DiagMon))
    MCU firmware
      UART driver
        RX buffer
        TX queue
      Diagnostic core
        Command router
        Channel filter
        1 ms tick
    PC console
      COM manager
      ANSI dashboard
        Header
        Metrics
        Event panel
      Session log
        Search
        Export
    Verification
      Loopback
      Soak test
      Release gate
```

## Timeline — 포팅 릴리스 이정표

```mermaid
timeline
    title UART 진단 도구의 진화
    2026-01 : Vendor sample 확보 : 핀맵과 baudrate 분석
    2026-02 : Kts_Uart API 매핑 : Ring buffer 도입
    2026-03 : Kts_DbgCore 통합 : KTS DBG 명령 처리
    2026-04 : ANSI dashboard : 실시간 전체 화면 갱신
    2026-05 : Release : 현장 검증 패키지 배포
```

## Quadrant chart — 개발 우선순위

```mermaid
quadrantChart
    title MCU 포팅 작업 우선순위
    x-axis Low effort --> High effort
    y-axis Low impact --> High impact
    quadrant-1 Strategic investment
    quadrant-2 Quick wins
    quadrant-3 Defer
    quadrant-4 Fill the gap
    "UART mapping": [0.25, 0.88]
    "ANSI dashboard": [0.72, 0.92]
    "Log export": [0.45, 0.62]
    "Theme polish": [0.18, 0.32]
    "Soak testing": [0.78, 0.66]
```

## XY chart — 처리량과 지연시간

```mermaid
xychart-beta
    title "UART frame throughput"
    x-axis [1, 2, 3, 4, 5, 6]
    y-axis "frames per second" 0 --> 120
    bar [42, 58, 71, 84, 96, 108]
    line [35, 52, 68, 80, 91, 104]
```

## C4 context — 진단 시스템 컨텍스트

```mermaid
C4Context
    title Kts DiagMon 진단 시스템
    Person(developer, "Developer", "펌웨어와 진단 결과를 확인한다")
    System(pc, "Kts DiagMon", "COM 연결과 ANSI dashboard를 제공한다")
    System(mcu, "Target MCU", "UART 진단 명령을 처리한다")
    SystemDb(log, "Session Log", "검증 결과와 이벤트를 저장한다")
    System_Ext(serial, "USB-UART", "MCU와 PC를 연결한다")
    Rel(developer, pc, "운영")
    Rel(pc, serial, "바이너리 프레임 송수신")
    Rel(serial, mcu, "UART")
    Rel(pc, log, "기록")
    Rel(mcu, pc, "상태 응답")
```

## Sankey diagram — 진단 데이터 흐름

```mermaid
sankey-beta
    MCU,PC,70
    MCU,Diagnostics,30
    PC,UART RX,42
    PC,UART TX,28
    Diagnostics,ANSI,16
    Diagnostics,Log,9
    Diagnostics,Errors,5
    UART RX,Dashboard,30
    UART RX,Log,12
    UART TX,Command,20
    UART TX,Retry,8
```

## Block diagram — 공식 예제 스타일의 MCU 진단 아키텍처

```mermaid
block-beta
    columns 4
    MCU(("MCU")) UART["UART Driver"] CORE[["Kts Dbg Core"]] PC[/"PC Tool"/]
    RX[("RX Buffer")] ROUTER{{"Command Router"}} ANSI>"ANSI Dashboard"] LOG[("Session Log")]
    TIMER(("1 ms Timer")) TEST[["Test Harness"]] EXPORT["Export"]
    MCU --> UART
    UART --> RX
    RX --> ROUTER
    ROUTER --> CORE
    PC --> ROUTER
    CORE --> ANSI
    CORE --> LOG
    TIMER --> CORE
    TEST --> UART
    LOG --> EXPORT
    classDef firmware fill:#dbeafe,stroke:#2563eb,stroke-width:3px,color:#172554
    classDef transport fill:#dcfce7,stroke:#16a34a,stroke-width:3px,color:#14532d
    classDef console fill:#fef3c7,stroke:#d97706,stroke-width:3px,color:#78350f
    class MCU,CORE,TIMER firmware
    class UART,RX,ROUTER,TEST transport
    class PC,ANSI,LOG,EXPORT console
    style CORE fill:#c4b5fd,stroke:#7c3aed,stroke-width:5px
    style ANSI fill:#f9a8d4,stroke:#db2777,stroke-width:4px
```
