# 월드 편집 UI 프리팹 저작 구조

## 요약

- **날짜:** 2026-07-20
- **담당:** Codex / 프로젝트 팀
- **상태:** 검증 완료
- **관련 요청:** 중복 스크립트 UI 없이 월드 편집 UI 전체를 hierarchy/prefab에서 직접 조정 가능하게 전환

## 의도

디자이너가 Unity에서 월드 편집 UI의 레이아웃, 글꼴, 스프라이트, 버튼 터치 영역,
드롭다운 템플릿을 직접 조정하면서도, 온톨로지 데이터 목록은 동적으로 보여 줄 수 있게 합니다.

## 데이터 분류

- [x] Unity 표현

## 결정과 경계

`WorldEditHUD.prefab`은 트리플/규칙/결과 행 템플릿과 물리 상세 팝업을 포함한 온톨로지
편집창의 고정 hierarchy를 소유합니다. `WorldEditContextHandle.prefab`은 선택 오브젝트
아이콘 툴바 hierarchy를 소유합니다. 런타임 바인더는 데이터 연결과 데이터 개수만큼의
작성된 행 템플릿 복제만 할 수 있으며, 시각 컨트롤과 레이아웃을 생성하지 않습니다.

`WorldEditHUD`를 다시 생성하던 기존 에디터 빌더는 제거했습니다. 설정 도구도 두 번째 UI
트리를 몰래 만들지 않고 프리팹 인스턴스가 없다는 사실을 알려 줍니다.

## 온톨로지 표현

없음. 이 변경은 Unity 표현 저작 구조만 다루며 Fact, Profile, Rule Block, 추론은 바꾸지 않습니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| 프리팹 자산 | HUD와 컨텍스트 핸들을 직접 열고 수정 가능 | Unity MCP로 두 프리팹 hierarchy 확인 |
| 런타임 진입 | 기존 편집기가 누락된 바인딩 없이 시작 | Play Mode 진입 후 Unity Console 오류/경고 없음 |
| 중복 생성기 제거 | 설정 도구가 두 번째 HUD를 만들지 않음 | `FarmWorldEditUiBuilder` 제거, 프리팹 인스턴스 누락 시 안내 |

## 성능과 멀티플레이 영향

새 polling, allocation 패턴, DB 쓰기, authority, Zone 영향은 없습니다.
행은 보이는 작성 데이터의 개수에 맞춰서만 여전히 복제됩니다.

## 갱신한 문서

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] 언어팩 CSV / migration 불필요
- [ ] 테스트 시나리오 목록 불필요
