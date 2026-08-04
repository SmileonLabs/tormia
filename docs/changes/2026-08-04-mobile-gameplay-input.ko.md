# 모바일 게임 플레이 입력

## 결정

Android와 iOS 게임 입력은 프로젝트 공용 Input System 액션 자산을 단일
장치 바인딩 원본으로 사용한다. Unity 입력은 트리거 어댑터 역할만 하며 이동,
점프, 장착, 기본 포인터 입력은 기존 canonical intent, 규칙 블록, World
Authority 경계를 그대로 통과한다.

## 구현

- `OntologyInputSystemPlayerInput`은 키보드 전용 액션을 런타임에 다시 만들지
  않고 프로젝트 공용 액션을 복제한다.
- 프로젝트 소유 모바일 조작 프리팹은 이동, 점프, 장착용 가상 Gamepad 입력을
  발생시킨다. 세션이 `InWorld`일 때만 보이고 가로 화면 safe area를 따르며
  `TormiaUI`에서 후보정할 수 있다.
- 게임 조작 UI 터치는 월드 포인터 클릭은 소비하지만 modal UI처럼 이동 전체를
  차단하지 않는다. 다른 UI의 입력 차단은 유지한다.
- 모바일 조작 UI는 이동, 장착, 피해 같은 게임 결과를 직접 적용하지 않는다.

## 검증

필수 터치·Gamepad 바인딩, 프리팹 control path, 가로 화면 safe area 변환을
테스트한다. 기존 규칙 블록 제거 경로는 변경하지 않는다.
