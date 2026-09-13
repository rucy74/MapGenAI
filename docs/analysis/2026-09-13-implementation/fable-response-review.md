# Claude consultation

Requested: claude-fable-5-1

Observed modelUsage: ["claude-haiku-4-5-20251001", "claude-fable-5-1"]

## 검토 결과: 첨부 범위(SimpleJson / ProviderResponse / RequestGate)에서 "실제 재현 가능한 중요 결함"은 찾지 못했습니다.

요구사항 네 가지를 기준으로 코드 경로를 따라가며 실패 입력을 만들어 봤지만, 모두 예외로 거부되거나 의도대로 폐기됩니다. 아래는 확인한 근거와, 중요도는 낮지만 알아둘 만한 관찰 사항입니다.

### 요구사항별 확인

**malformed / 잘림 / Unicode / empty-array**
- 잘린 입력 `{"a":[1,` `{"a":"abc` `{"a":1` 은 각각 `Incomplete JSON value` / `Unterminated string` / `Expected ','` 로 FormatException. 부분 복구 없음.
- 트레일링 콤마 `{"a":1,}` `[1,]`, 선행 0 `01`, `-` `1.` `.5` `+1` `1e` `Infinity` 모두 거부. `1e400` 은 double 파싱 단계에서 Non-finite로 거부.
- 중복 키 거부, 깊이 49 이상 거부, 1MB 초과 거부, 루트가 객체가 아니면 거부, 트레일링 데이터 거부.
- Unicode: raw/escaped 혼합 서로게이트 쌍 모두 처리. 고립 high/low 서로게이트, `\uD83D` 뒤 non-`\u` 는 모두 거부. `\u0000` 같은 escaped 제어문자는 허용, raw 제어문자는 거부. 직렬화 시 서로게이트를 `\uXXXX`로 개별 이스케이프하므로 출력은 항상 유효한 JSON.
- 빈 배열: `candidates: []` `choices: []` `parts: []` 는 각각 "no candidate" / "no choices" / "no text"로 거부. `content: []` (OpenAI 배열형)도 빈 문자열이 되어 "no text".

**token-limit 출력 거부**
- Gemini `finishReason != "STOP"`, OpenAI `finish_reason != "stop"` 은 예외. `MAX_TOKENS` / `length` 모두 걸립니다. 값이 JSON `null`이거나 누락되면 통과하는데, 이는 비스트리밍 최종 응답에서는 발생하지 않는 형태입니다.

**Reset/닫기 이후 늦은 응답 폐기**
- `Cancel()` 이 lock 안에서 `version++` + `pending=null` 을 먼저 하므로, 이후 도착하는 `Complete(구 ticket)` 는 버전 불일치로 버려지고, 이미 큐잉된 reply도 함께 사라집니다. `Begin()` 이 `Cancel()` 을 먼저 호출하므로 재시작 시에도 동일.
- `old.Cancel()` 후 `old.Dispose()` 순서 덕분에, 워커가 그 사이에 `Register`/`CreateLinkedTokenSource`/`Task.Delay(…, token)` 을 호출해도 이미 취소된 토큰으로 처리되어 ObjectDisposedException은 나지 않습니다. `IsCancellationRequested` 도 Dispose 후 안전.

**설정 변조**
- `ConvertTo<T>` 의 대상 타입은 코드가 고정하고, 알 수 없는 필드는 무시, 타입 불일치는 예외. 페이로드가 CLR 타입명이나 필드 외 멤버를 건드릴 경로는 없습니다.

### 중요도 낮은 관찰 (결함이라기보다 유의점)

1. **문자열↔스칼라 느슨한 강제.** `GetBool` 은 문자열 `"true"` 도 true로 봅니다. 최소 입력: Gemini part `{"thought":"true","text":"..."}` 가 thought로 간주되어 텍스트가 탈락하고 "no text" 오류. 결과는 거부이므로 안전하지만 의미상 오탐입니다. 같은 이유로 OpenAI `"content": 123` 은 텍스트 `"123"` 으로 통과하고, 이후 `Command()` 에서 루트 객체가 아니어서 안전하게 거부됩니다.
2. **Serialize 가 `char`/`DateTime` 필드를 따옴표 없이 출력.** `IConvertible` 분기가 이를 문자열처럼 감싸지 않습니다. DTO에 해당 타입 필드가 있을 때만 유효하지 않은 JSON이 생성됩니다. 첨부 범위에는 그런 DTO가 없어 재현 불가.
3. **BOM(U+FEFF) 로 시작하는 본문은 거부.** `Whitespace()` / `Trim()` 모두 제거하지 않아 `Unexpected JSON value`. HttpClient 문자열 디코딩이 보통 BOM을 제거하므로 실제 발생 가능성은 낮고, 발생해도 fail-closed입니다.

### 범위 밖이지만 한 줄 표시

설명하신 워커 흐름은 OperationCanceledException만 명시적으로 잡습니다. `ProviderResponse` 의 FormatException 이나 HttpRequestException 이 마지막 fallback에서 잡히지 않고 `Complete` 를 건너뛰면 UI `_isWaiting` 이 영구 true가 되어 "무한대기"가 됩니다. RequestGate 자체는 이를 막을 수단이 없으므로, 워커의 catch-all 여부는 별도 검토 범위에서 확인이 필요합니다.