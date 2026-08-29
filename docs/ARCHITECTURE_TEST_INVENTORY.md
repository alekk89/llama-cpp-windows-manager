# Architecture-test inventory

| Existing rule | Classification | Replacement/status |
| --- | --- | --- |
| Verified session stop | Process lifecycle | Source guard replaced by fake-runtime and stop-verification behaviour tests |
| Thin MainWindow launch/overview adapters | Composition ownership | Replaced by compiled field-type inspection plus independent WPF behaviour tests |
| UI Common/Pages placement | Source organization | Documented layout guard |
| Unique service/UI filenames | Source organization | Reviewability guard |
| Launch-settings factory split | Source organization | File/size layout guard retained; compiled public-contract checks replace source strings |
| Model-group dialog split | Source organization | File/size layout guard retained; compiled method checks replace source strings |
| MainWindow partial bounds | Source organization | Temporary guard with measured failure output |
| Control runtime ownership | I/O ownership | Compiled dependency/shell-field inspection plus application-service behaviour tests |
| Gateway transport separation | Security/lifecycle | Compiled dependency inspection plus access-policy/transport behaviour tests |
| Runtime deletion separation | I/O ownership | Compiled public-method boundary plus path-safety and planning behaviour tests |
| Update helper separation | Security/lifecycle | Source-shape check removed; manifest, substitution, truncation, rollback, locked-file, and publisher tests enforce behaviour |
| Filesystem work off UI | I/O ownership | Async filesystem behaviour retained; source guard temporary |
| Control settings ownership | Application policy | Compiled context/property boundary plus mutation behaviour tests |
| Control/API/theme decomposition | Source organization | Compiled endpoint-field checks; file/resource layout guard retained |
| Core portability | Assembly dependency | Replaced by compiled assembly-reference test |
| ViewModel I/O boundary | I/O ownership | Replaced by compiled IL symbol analysis and canary |
| UI wiring and visual tokens | Composition/presentation | Exact source-string inventories removed; composed WPF surfaces and service/controller behavior retain coverage |
| Source size | Temporary refactoring guard | Generous review trigger pending a complexity metric |

Method renames and harmless file moves do not affect compiled-symbol tests.
The remaining source checks protect deliberate repository/file layout,
packaging/security contracts, or lifecycle rules that cannot be observed safely
through a compiled test. Layout guards may be removed after behavioural parity;
coverage and zero-skipped policies remain unchanged.
