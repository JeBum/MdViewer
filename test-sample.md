---
title: MDviewer 통합 기능 검증 가이드
version: 2.0.0
last_updated: 2026-09-17
tags: [markdown, viewer, katex, json, editor-sync, callouts]
status: verified
---

# MDviewer 통합 기능 검증 문서

이 문서는 MDviewer의 모든 핵심 렌더링 엔진 및 편집기 기능(LaTeX 수식, JSON 뷰어, 콜아웃, 태스크 체크박스, 양방향 동기화 등)을 종합적으로 검증하기 위한 테스트 샘플입니다.

---

## 1. LaTeX 수식 (KaTeX) 렌더링 테스트

### 1.1 인라인 수식 (Inline Math)
- 질량-에너지 등가 방정식: $E = mc^2$
- 2차 방정식 근의 공식: \(x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}\)
- 오일러 공식: $e^{i\pi} + 1 = 0$ 및 $\lim_{n \to \infty} \left(1 + \frac{1}{n}\right)^n = e$

### 1.2 블록 수식 (Display Math)
$$
\int_{-\infty}^{\infty} e^{-x^2} dx = \sqrt{\pi}
$$

$$
f(x) = \sum_{n=0}^{\infty} \frac{f^{(n)}(a)}{n!} (x - a)^n
$$

행렬(Matrix) 표현:
$$
\begin{bmatrix}
a_{11} & a_{12} & \cdots & a_{1n} \\
a_{21} & a_{22} & \cdots & a_{2n} \\
\vdots & \vdots & \ddots & \vdots \\
a_{m1} & a_{m2} & \cdots & a_{mn}
\end{bmatrix}
$$

---

## 2. JSON 뷰어 & 구문 강조 (One Dark Pro)

### 2.1 한 줄 Minified JSON (자동 2칸 들여쓰기 정렬)
```json
{"battery_id":"BAT-2026-X1","chemistry":"LFP한글","nominal_voltage":3.2,"capacity_ah":100,"soc_percent":85.5,"is_active":true,"fault_flags":null}
```

### 2.2 복합 중첩 객체 & 배열 (Syntax Highlighting & Copy 버튼)
```json
{
  "system_info": {
    "device_name": "MDviewer-Engine",
    "version": "2026.09.17",
    "status": "operational",
    "debug_mode": false,
    "metrics": {
      "fps": 60,
      "latency_ms": 1.25,
      "error_count": 0
    }
  },
  "cell_voltages": [3.28, 3.29, 3.27, 3.28, 3.30],
  "supported_features": [
    "katex_math",
    "json_prettifier",
    "task_checkbox_sync",
    "bidirectional_scroll"
  ],
  "owner": null
}
```

### 2.3 문법 오류 JSON (Syntax Error 배지 & 오류 위치 안내)
```json
{
  "invalid_json": "missing closing bracket",
  "data": [1, 2, 3
}
```

---

## 3. 코드 블록 & 원클릭 복사 (General Code Blocks)

```python
def calculate_fibonacci(n: int) -> list[int]:
    """피보나치 수열 생성 함수"""
    if n <= 0:
        return []
    sequence = [0, 1]
    while len(sequence) < n:
        sequence.append(sequence[-1] + sequence[-2])
    return sequence[:n]

print(calculate_fibonacci(10))
```

```csharp
public sealed class MarkdownRenderer
{
    public string Name { get; init; } = "MDviewer";
    public bool EnableKaTeX { get; set; } = true;
    
    public void Render(string markdown) =>
        Console.WriteLine($"Rendering with {Name}...");
}
```

---

## 4. GitHub 스타일 콜아웃 (Callouts / Alerts)

> [!NOTE] 알림
> MDviewer는 서강대학교 테마와 알바트로스 테마를 완벽하게 지원합니다.

> [!TIP] 팁
> `Shift + F8` 키를 누르면 현재 선택된 단어가 실시간 형광 강조 키워드로 등록됩니다.

> [!IMPORTANT] 중요
> 편집 모드에서 텍스트를 수정하면 우측 미리보기 뷰가 깜빡임 없이 즉시 동기화됩니다.

> [!WARNING] 주의
> 문서를 닫기 전 변경 사항이 저장되었는지 확인하세요.

> [!CAUTION] 경고
> 원본 파일 삭제 또는 권한 변경 시 저장이 실패할 수 있습니다.

> 조심하랑께 그랴 조심하고 사러.
---

## 5. 대화형 태스크 체크박스 (Interactive Task List)

미리보기 뷰에서 체크박스를 클릭하면 편집 모드 소스의 `[ ]`와 `[x]`가 즉시 상호 연동됩니다.

- [x] KaTeX 수식 렌더러 연동 완료
- [x] JSON 자동 포맷팅 & One Dark Pro 테마 적용
- [x] 편집기 커서 위치-미리보기 실시간 스크롤 동기화
- [ ] 다음 릴리즈 기능 테스트
  - [x] 마크다운 각주 (Footnotes) 연동
  - [ ] 추가 테마 커스터마이징

---

## 6. 키워드 강조 (Highlight) 및 단축키 안내

### 6.1 본문 강조
==이 문장은 하이라이트 문법으로 강조된 영역입니다.==

### 6.2 지원 단축키
| 기능 | 단축키 | 설명 |
| :--- | :--- | :--- |
| **단어 검색** | `Ctrl + F` | WebView2 내장 검색창 열기 |
| **단어 강조 토글** | `Ctrl + F8` | 선택 단어 강조 추가, 다시 누르면 삭제 (Toggle) |
| **편집 모드 토글** | `F8` | 1회 클릭 시 편집 모드 진입, 다시 클릭 시 편집 모드 및 분할 취소 |
| **굵게 (Bold)** | `Ctrl + B` | 선택 영역 **굵게** 적용 |
| **기울임 (Italic)** | `Ctrl + I` | 선택 영역 *기울임* 적용 |
| **취소선 / 링크** | `Ctrl + K` | 선택 영역 [링크](https://google.com) 생성 |
| **저장** | `Ctrl + S` | 현재 파일 저장 |

---

## 7. 각주 (Footnotes) 테스트

Markdown은 직관적이고 강력한 문서 작성 포맷입니다[^md-origin]. 또한 MDviewer는 대학원 연구 및 프로그래밍 수업에 최적화되어 있습니다[^md-sogang].

[^md-origin]: 마크다운은 존 그루버(John Gruber)가 2004년에 만든 경량 마크업 언어입니다.
[^md-sogang]: 서강대학교 MOT대학원 데이터 엔지니어링 프로그래밍 과정 지원.

---

## 8. 테이블 (Markdown Tables)

| 모듈명 | 지원 사양 | 상태 | 비고 |
| :--- | :--- | :---: | :--- |
| **수식 엔진** | KaTeX 0.16.11 (인라인/블록) | ✅ 정상 | 즉시 렌더링 |
| **JSON 포맷터** | Auto-Prettify (2-space) | ✅ 정상 | One Dark Pro |
| **양방향 동기화** | Click-to-Line & Cursor Sync | ✅ 정상 | 선형 보간 적용 |
| **단일 인스턴스** | IPC Named Pipe 연동 | ✅ 정상 | 탭으로 자동 추가 |

---

## 9. 이미지 캡션 및 검색 연동 테스트

![MDviewer 알로스](https://www.sogang.ac.kr/_nuxt/img2.0e15cb8f.svg)

![MDviewer 서강이](https://www.sogang.ac.kr/_nuxt/img1.eae39140.svg)

