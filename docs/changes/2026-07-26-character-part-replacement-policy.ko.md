# 캐릭터 파츠 교체 정책

## 요약

- 날짜: 2026-07-26
- 상태: 구현 및 검증
- 범위: 계정 외형 데이터와 Unity 표현

## 의도

파츠 섬네일은 예측 가능한 교체와 토글로 동작해야 합니다. Body와 Face는 항상
유지하지만, 그 밖의 착용 파츠는 선택된 섬네일을 다시 눌러 해제할 수 있습니다.
여러 슬롯을 덮는 의상을 벗을 때 이전 의상을 자동으로 복원하지 않습니다.

## 데이터 소유권

선택한 파츠 ID는 계정 소유 캐릭터 프로필 데이터로 유지합니다. canonical 슬롯,
`required`, 연결 파츠 ID, `conflicts_with_slot` Fact가 교체 동작을 정의합니다.
Unity는 렌더러 연결과 미리보기 표현만 담당합니다.

## 결정

- canonical `Body`와 `Face` 정의만 필수이며 직접 해제를 거부합니다.
- Hair, 상의·하의, 신발, 전신 의상, 겉옷, 모자, 안경, 장갑, 수염, 액세서리는
  모두 선택 사항입니다.
- 같은 슬롯의 다른 파츠를 선택하면 기존 파츠를 교체하고, 이미 장착한 선택 파츠를
  다시 누르면 해제합니다.
- 여러 슬롯을 덮는 파츠는 현재 장착된 충돌 파츠를 해제하지만 복원 이력은 기록하지
  않습니다. 덮는 파츠를 교체하거나 해제해도 이전 착용 파츠는 자동 복원되지 않습니다.
- 전신 의상 정의는 전용 `Full_body` 렌더러에 연결합니다.
- 변형 적용 시 메시·머티리얼·원본 로컬 bounds를 함께 복사합니다.
- 기본 정의는 캐시한 템플릿 렌더러 상태를 복원합니다.
- 가입용 미리보기 복제본은 활성 상태와 머티리얼뿐 아니라 메시와 로컬 bounds도
  동기화합니다.
- 미리보기는 파츠 변경 완료 이벤트를 구독하여 여러 슬롯을 덮는 파츠의 표시 상태를
  같은 클릭 안에서 동기화합니다.
- 선택지가 하나뿐인 기본 신체와 임시 상의 메시는 내부 연결로 유지하되 꾸미기
  카테고리 목록에서는 숨깁니다.
- 충돌 교체는 실제 장착 중인 정의만 해제합니다. 장착되지 않은 코스튬 정의가 연결
  파츠를 통해 공용 모자·의상 렌더러를 끌 수 없습니다.
- 일반 모자는 연결된 코스튬 모자만 교체하며 전신 코스튬은 유지합니다.
- 에디터 시드 카탈로그도 런타임 데이터와 동일한 `Full_body` 렌더러 경로와 충돌
  정책을 사용하여 재생성 시 문제가 되살아나지 않게 합니다.

## 검증

| 경우 | 기대 결과 | 증거 |
| --- | --- | --- |
| Body 또는 Face 재클릭 | 장착 상태 유지 | `OntologyCharacterPartAdapterTests.ClothingAndFootwearCanBeRemovedWhileBodyAndFaceRemainRequired` |
| 의상 또는 신발 재클릭 | 장착 해제 | `OntologyCharacterPartAdapterTests.ClothingAndFootwearCanBeRemovedWhileBodyAndFaceRemainRequired` |
| 전신 의상을 상의로 교체 | 이전 하의는 해제 상태 유지 | `OntologyCharacterPartAdapterTests.ReplacingFullBodyWithUpperBodyDoesNotRestorePreviousLowerBody` |
| 연결 코스튬 해제 | 코스튬·연결 파츠·밀려난 의상 모두 해제 상태 유지 | `OntologyCharacterPartAdapterTests.LinkedCostumePartsEquipAndUnequipAtomically` |
| 변형과 기본 파츠 교체 | 원본 bounds 복사 및 기본 메시 복원 | `OntologyCharacterPartAdapterTests.VariantCopiesAuthoredLocalBoundsAndBaseSelectionRestoresOriginalMesh` |
| 가입 미리보기 변형 | 복제 메시와 bounds가 원본과 일치 | `TormiaUiSceneSmokeTests.CompositionCreatesOntologyWorldAndUsesSingleUiScene` |
