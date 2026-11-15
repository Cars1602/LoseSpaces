# Luna Cero: supervivencia y hordas

La escena principal es `Assets/LunaCero/Scenes/LunaCeroMapaGrande.unity` (mapa grande de esenario2,
primera en Build Settings). Se conservan `LunaCeroEscenario.unity` (mapa pequeno), la expedicion
procedural `LunaCeroSurvival.unity` y la arena anterior para probar combate aislado.

## Multijugador (2 a 4 jugadores, LAN u online con AWS)

Al empezar sale el menu: **Jugar solo**, **LAN** (crear sala o unirse por lista/IP) u **Online**
(crear sala o unirse desde la lista del servidor 107.23.18.43). Despues cada uno elige su rol.

- Quien crea la sala es el **anfitrion**: su PC simula monstruos, hordas, objetos y piezas.
  Cada jugador mueve a su astronauta; disparos, golpes y recogidas se envian al anfitrion.
- Todos se conectan a un **relevo** que reenvia los paquetes. En LAN va dentro del juego del
  anfitrion (UDP 7778; busqueda de salas UDP 47778). Online es `Server/relay.py` en AWS, lanzado
  por el servicio `cubos-online` con el perfil `losesspaces-4` (UDP 7800-7803, API HTTPS 443 con
  certificado fijado por huella). No hace falta compilar el juego para Linux.
- Se puede entrar con la partida empezada (hasta 4). Si el anfitrion sale, la sala se cierra.
  F10 sale de la sala. Al morir en red, Enter reaparece en la base sin reiniciar el mapa.
- Los monstruos persiguen al tripulante mas cercano; no hay fuego amigo. Los companeros salen
  en el radar (celeste) con su nombre y rol sobre la cabeza.
- Scripts: `Scripts/Net/LunarNet.cs` (UDP, relevo LAN, mensajes fiables), `LunarSession.cs` (menu,
  API de salas), `LunarCoop.cs` (sincronizacion), `LunarPuppet.cs` (astronautas remotos), `LunarCrew.cs`.
- Limitaciones: los agarres especiales (lengua, montarse, embestida) solo atrapan al anfitrion; a los
  demas solo les hacen dano. Todos deben usar la misma compilacion y el mismo mapa.
- Actualizar el relevo en AWS: copiar `Server/relay.py` a `/opt/cubos-online/lunar-relay/relay.py`.
  Copia del gestor anterior: `/opt/cubos-online/lobby-before-relay.py`.

## Mapa grande (Assets/esenario2)

- `esenario2/esenario/MapaGrande.unity` usa un terreno, prefabs de rocas/piezas y scripts que no
  existen en este proyecto (vienen del proyecto donde se creo), asi que no se abre bien tal cual.
  Sus posiciones se extrajeron a `Assets/LunaCero/Data/MapaGrande.json` y
  **Luna Cero > Crear mapa grande (esenario2)** reconstruye la escena (tambien se ejecuta sola una vez).
- 400 x 400 m centrados en la base original. Terreno de Unity generado con las alturas del mapa
  (rocas, escombros y puntos apoyados en el suelo), 4 crateres en sombra excavados con hielo y nidos,
  y el terreno original con su crater en el centro. 450 rocas, 114 escombros de la Condor (campo y
  rastros que llevan a las piezas), fondo de montanas y la Tierra a escala del mapa.
- Las 5 piezas estan lejos de la base, en los puntos del mapa grande. La nave se repara en la base.
- Navegacion horneada en el editor (`Scenes/MapaGrande/Navegacion.asset`): no hay espera al empezar.
- Los monstruos aparecen en 26 puntos del mapa a entre 22 y 80 m del jugador.
- `esenario2/Nueva carpeta` es una copia identica de `Assets/esenario` (mismos GUID): Unity les
  asigna GUID nuevos al importarla. Se puede borrar sin perder nada.

## Armas repartidas y objetos contundentes

- No hay armero: 10 armas de fuego (rifle, escopeta, riel, plasma), 11 objetos contundentes,
  22 cajas de municion y 8 botiquines repartidos por el mapa. Brillan un poco para verlos a oscuras.
- Objetos contundentes (`LunarWeaponData.MeleeDefaults`): llave inglesa, tubo de metal, extintor,
  barra de la nave y **escombros de la nave** (la mitad de los trozos pequenos de los rastros se pueden
  agarrar). Clic: golpe corto que aturde; no hace ruido para la horda; se gastan con el uso
  (el HUD muestra los usos). En la mano se ven con la forma del objeto recogido.
- Junto a la base hay una llave inglesa y un tubo para empezar.

## Roles

Al empezar se elige rol (1-5 o clic). Se recuerda al reiniciar; al morir, C permite cambiarlo.
Todos pueden recoger armas y objetos contundentes.

| Rol | Funcion |
| --- | --- |
| Piloto | Propulsor +70% combustible y recarga x1.5, algo mas rapido, ve la nave a cualquier distancia |
| Militar | Vida 125, +25% dano, recarga 25% mas rapida, empieza con rifle y mas municion |
| Explorador | Radar de 85 m con armas/municion/botiquines, +18% velocidad, +60% energia, -25% oxigeno |
| Medico | 4 botiquines que curan mas (F en cualquier lugar), regenera vida, sana en estaciones en 1.5 s |
| Ing. aeronautico | Instala piezas desde el doble de distancia, ve todas las piezas, hielo x2, casillero x0.5, llave inglesa |

Los valores estan en `LunarRoles.cs` (`LunarRoleDefinition.All`).

## Rendimiento

`LunarPerformance` (se anade solo al jugador): 60 FPS objetivo, sombras a 55 m con 2 cascadas,
4 luces por pixel, linterna con sombras duras, luces de puntos lejanas (mas de 60 m) apagadas,
terreno con menos detalle lejano. Rocas y escombros son estaticos y dejan de dibujarse cuando son
muy pequenos en pantalla. Las barras de vida de los monstruos solo se dibujan a menos de 40 m
(jefes 90 m) y los monstruos lejanos no lanzan rayos de vision.

## Monstruos mas grandes

`LunarModelInstaller.SizeScale = 1.35`: Emjambre 2.2 m, Escupidor 3.1 m, Gigante 4 m,
Asechador 2 m. La capsula de impactos y el agente crecen con el modelo; el alcance de sus golpes
tiene en cuenta su anchura. Cambia el numero y usa **Luna Cero > Instalar modelos** para reajustar.

## Expedicion actual

- Mundo plano procedural: una superficie circular de 118 m de radio y sectores
  cargados alrededor del jugador. El centro se desplaza al avanzar; los sectores
  lejanos se descargan y se actualiza la navegacion. No hay muros exteriores.
  La niebla oculta el borde de carga. Esto no es todavia una Luna esferica ni
  incluye reajuste de origen para viajes de miles de kilometros.
- Empiezas con 12 tiros en el cargador y CERO de reserva. E recoge cajas azules
  de 18 balas. R transfiere solo las balas disponibles; un cargador vacio no se
  rellena solo. Las cajas consumidas no reaparecen al volver a un sector durante
  la misma partida. Reiniciar la partida restablece los suministros.
- Solo puedes sanar dentro del anillo verde de una estacion medica: F inicia
  tres segundos de curacion. Salir del anillo, recibir dano o quedar inmovilizado
  interrumpe el proceso. La estacion se recarga durante 25 segundos despues del
  uso. El HUD indica direccion y distancia a la estacion mas cercana.
- Los aliens comunes persiguen al jugador. El director alterna patrullas y
  hordas con limite de 28 enemigos vivos; dispara una oleada especial cada 38 s.
  Los disparos alertan a los aliens cercanos y la contaminacion atrae refuerzos.
- Q empuja a enemigos cercanos. Durante un agarre, tres pulsaciones de Q
  permiten escapar en este modo para un jugador. Matar al captor tambien libera.
  T enciende la linterna, que puede provocar a la Durmiente.

| Alien provisional | Mecanica |
| --- | --- |
| Contaminador | Proyectil de bilis que marca y atrae horda; estalla al morir cerca |
| Tentaculo (jefe) | Captura a distancia, arrastra y causa dano; deja humo al morir |
| Saltador | Salta hacia el jugador, inmoviliza y ataca |
| Durmiente (jefe) | Solo despierta por proximidad, dano o linterna; corre y derriba |
| Coloso (jefe) | Gran resistencia, rocas, golpes con retroceso y empuja objetos |
| Embestidor (jefe) | Carrera anunciada, captura y aplastamiento; resiste empujones |
| Escupidor | Proyectil que deja acido; el dano aumenta mientras permaneces encima |
| Parasito | Salta, se monta y fuerza el movimiento mientras ataca |

Los proyectiles y los agarres respetan las paredes. Los especiales tienen
siluetas y colores provisionales; faltan modelos, animaciones y audio propios.
La logica esta inspirada en las mecanicas descritas por el usuario, con nombres
y representaciones de aliens. No incluye cooperativo, companeros de IA ni
el director completo de Left 4 Dead 2. Al eliminar los cuatro tipos de jefe se
registra el objetivo; la exploracion y las hordas continuan.

`Luna Cero > Crear expedicion de supervivencia` genera esta escena y los nueve
prefabs de aliens (un comun y ocho especiales), y la establece como escena de
compilacion. Sobrescribe esos assets al regenerar.
`Probar supervivencia automaticamente` comprueba en Play mundo, balas, estaciones,
persecucion y las ocho habilidades. Busca `LUNA_SURVIVAL_SMOKE_OK` en Console.

## Arena anterior

Abre `Assets/LunaCero/Scenes/LunaCeroCombat.unity` y pulsa Play. Esta es la escena incluida en Build Settings. Conservamos `LunaCeroPrototype.unity` como escenario anterior de movimiento.

## Controles

| Accion | Control |
| --- | --- |
| Caminar | WASD o flechas |
| Correr | Shift izquierdo |
| Salto lunar | Espacio |
| Girar y orientar la camara | Raton |
| Disparar | Clic izquierdo, mantener para repetir |
| Recargar | R |
| Curarse | F |
| Recoger suministros / levantar caja / soltar caja | E |
| Lanzar caja | G |
| Liberar / capturar cursor | Escape / clic izquierdo |
| Reiniciar despues de morir | Enter |

La camara sigue por detras del astronauta y se acerca al encontrar paredes. La mira orienta los disparos; tambien comprobamos el tiro desde el arma para que las paredes cercanas bloqueen el disparo. Cada tiro hace 25 de dano, hay 12 tiros por cargador y 72 de reserva inicial. La recarga tarda 1,25 s.

El jugador tiene 100 de vida y 3 botiquines iniciales. Cada botiquin recupera hasta 40 puntos y no se consume con vida completa. Las cajas verdes dan botiquines, las azules municion y las amarillas se pueden cargar y lanzar. Los suministros cercanos se recogen mirando hacia ellos, sin atravesar paredes.

## Enemigos provisionales

Un unico prefab de bot debil se repite ocho veces. Los cuatro jefes tienen prefabs separados y colores diferentes.

| Enemigo | Vida | Ataque |
| --- | --- | --- |
| Bot debil | 60 | Cuerpo a cuerpo, 10 de dano |
| Custodio | 250 | Cuerpo a cuerpo, 22 de dano |
| Centinela | 220 | Disparo, 12 de dano |
| Depredador | 180 | Persecucion rapida, 14 de dano |
| Comandante | 300 | Rafagas de 3 disparos, 8 de dano por tiro |

La navegacion se construye al comenzar usando suelo, paredes y coberturas estaticas. Los bots persiguen al detectar al jugador o recibir dano. Los ataques respetan paredes. La muerte desactiva ataques y colliders del enemigo. Al morir el jugador pierde sus acciones y puede reiniciar. La victoria requiere eliminar a los cuatro jefes y ocho bots.

## Personaje y recursos

`Prefabs/LunarPlayer.prefab` conserva `Assets/correr/correr.fbx`, su textura, un Rigidbody y exactamente cuatro BoxCollider: cabeza, torso y dos piernas. El esqueleto animado no modifica la fisica. `run` se reproduce en bucle al moverse por el suelo. Reposo y salto usan una pose fija temporal. Curacion, disparo, carga y muerte tienen su logica; las animaciones especificas quedan pendientes. Arma y enemigos usan geometria provisional.

Los ajustes se editan en componentes del jugador y prefabs de enemigos.

## Herramientas del editor

- `Luna Cero > Crear arena de combate`: regenera la arena a partir del prototipo; configura jugador, enemigos y suministros. Sobrescribe esos assets y deja la arena como unica escena de compilacion. No usar sobre una arena personalizada sin copia.
- `Luna Cero > Validar arena de combate`: verifica componentes y cantidades.
- `Luna Cero > Probar combate automaticamente`: entra en Play y comprueba navegacion, persecucion, dano, curacion, recoger/lanzar, disparos, cobertura, recarga y muertes. Sale de Play al terminar. Busca `LUNA_COMBAT_SMOKE_OK` o `LUNA_COMBAT_SMOKE_FAILED` en Console. La prueba no se incluye en builds.
- `Usar personaje correr FBX`: reinstala modelo y animacion.
- `Crear escena de prueba`: restablece el prototipo anterior y su prefab.

## Nave de partida y armas

En la expedicion el jugador aparece dentro de la "Nave Aurora" (cubos provisionales creados por
`LunarShip` desde `LunarEndlessWorld`). Dentro hay:

- Armero (pared izquierda): Rifle de asalto, Escopeta, Rifle de riel y Lanzador de plasma. E para tomar.
- Casillero de municion (pared derecha): E restablece la reserva de todas tus armas (45 s de espera).
- Atmosfera presurizada: recarga el oxigeno mientras estes dentro.

| Arma | Tipo | Notas |
| --- | --- | --- |
| Pistola | Inicial | 25 de dano, 12 por cargador |
| Rifle de asalto | Automatica | 30 por cargador, leve dispersion |
| Escopeta | 9 perdigones | Corto alcance, 6 por cargador |
| Rifle de riel | Perforante | Atraviesa 4 enemigos, clic derecho = zoom |
| Lanzador de plasma | Explosiva | Dano en area 4,5 m, empuja objetos |

1-5 o la rueda del raton cambian de arma. Las cajas azules recargan el arma equipada (escaladas por arma).
Las estadisticas se editan en `LunarPlayerCombat > Arsenal` (prefab del jugador). Para reemplazar los cubos,
asigna `model` (arma en mano) y `pickupModel` (arma en el armero); la pistola usa `Pistol Model`.

## Escenario lunar (Assets/esenario)

`Luna Cero > Crear escenario lunar (esenario)` genera `Scenes/LunaCeroEscenario.unity` (primera escena de compilacion)
a partir de `Escenario_Completo.fbx`:

- Materiales con los colores de Blender (`Materials/Escenario`), luces emisivas y contornos tipo cartoon.
- Colisiones en terreno, base, nave estrellada y rocas; limites invisibles en el borde del terreno de 90 m.
- Base: zona presurizada (recarga oxigeno), estacion medica en la plataforma 3, armero y caja de municion (Caja_1).
- Cinco piezas de nave (Nucleo, Navegacion, Tanque, Motor en el crater, Comunicaciones en la cresta):
  E para cargarlas y soltarlas en el anillo verde de `Punto_Reparacion_Nave`. Las cinco = mision cumplida.
- Cristales de hielo del crater: E para extraer, +30 de oxigeno.
- Aliens desde `Spawn_Criatura_*` (crater y rocas), con navegacion construida al iniciar (`LunarLevelNavigation`).

## Vista, radar y linterna

- Primera persona por defecto (V alterna con tercera persona). `LunarViewModel` dibuja brazos del traje y guantes
  que sujetan el arma con las dos manos; balanceo al caminar, inercia, retroceso, apuntado y bajada al recargar.
  Los disparos salen de la boca del arma visible. El cuerpo solo proyecta sombra en primera persona.
- Radar (`LunarRadar`, arriba a la izquierda, 45 m, gira con la mirada): rojo = enemigos, punto de calor grande
  y palpitante = jefes, azul = piezas de la nave, verde = punto de reparacion. Piezas y jefes fuera de alcance
  se muestran en el borde.
- Escenario oscuro: sol al 0.22 y ambiente casi negro. La linterna (T) empieza encendida y es la luz principal.
- Barras abajo a la izquierda: vida, oxigeno, energia (solo se gasta corriendo en el suelo) y propulsor
  (combustible propio que se recarga al pisar el suelo).

## Jefes

| Jefe | Modelo | Habilidad |
| --- | --- | --- |
| Asechador (Pouncer) | Jockey/moustro asechador | Salta sobre el jugador y lo inmoviliza |
| Gigante (Brute) | Moustro Gigante | Lanza rocas y golpes con retroceso; muerte animada |
| Escupidor (Boomer / Bile) | Boomer | Lanza bilis sincronizada con su animacion de ataque; marca al jugador y atrae a la horda |
| Jockey (Rider) | Jockey/moustro asechador | Se monta y fuerza el movimiento |

Tentaculo, Durmiente, Embestidor y Acido (mismo modelo del Escupidor, mas pequeno) siguen como especiales (no jefes).
Propulsor: en el aire, pulsar Espacio otra vez y mantener gasta energia y eleva al astronauta (sirve para salir del crater).
Gravedad de juego 3.2 (la lunar real 1.62 era demasiado flotante) y salto de 0.85 m.

Hordas (`LunarHordeDirector`): el relleno (Moustro Emjambre) forma cada horda y patrulla cada 10 s.
Los jefes salen desde la horda 2 y luego cada 2 hordas; desde la horda 4 pueden llegar dos a la vez
(35 %), con un maximo de 2 jefes vivos. Todo se ajusta en el Inspector del director.

## Modelos 3D instalados

`Luna Cero > Instalar modelos (Emjambre y pistola)` (se ejecuta sola la primera vez):
- Moustro Emjambre (`Assets/Moustro Emjambre`): modelo de los aliens basicos `Common` (horda) y `BotDebil` (arena).
  Corre segun su velocidad y reproduce `atacarEmjambre` en cada golpe (`LunarAlienAnimation`).
- Pistola (`Assets/pistola`): prefab `Prefabs/Armas/Pistola.prefab`, asignado en `LunarPlayerCombat > Pistol Model`.
  Si apunta al reves, gira el hijo `Modelo` 180 grados dentro del prefab.

Agua, comida, radiacion, hielo y recuperacion de las cinco piezas de nave siguen pendientes.

## Animaciones del astronauta
Reposo, caminar, correr (Shift), saltar (Espacio), disparar (clic) y muerte usan los FBX de Assets/correr. Mantener C quieto reproduce cavar; aun no extrae recursos. Emote excluido. La muerte termina en su pose final. Recarga y curacion conservan sus funciones, sin clips propios disponibles. Menu: Luna Cero > Conectar animaciones (sin emote). El instalador verifica que los siete clips animan el esqueleto y conserva los cuatro colisionadores.


Cada estacion permite 3 curaciones completas por partida y espera 25 segundos entre usos. Interrumpir la curacion no gasta usos. El agotamiento y la espera persisten al salir y volver al sector. El indicador dirige a otra estacion con usos disponibles. Reiniciar la partida restablece los suministros.


El Parasito (Rider/Jockey) usa Assets/Jockey/moustro asechador/asechando.fbx y su textura original. Acecha al moverse, usa MuerteASE al morir y conserva salto/agarre con movimiento por codigo. Menu de reinstalacion: Luna Cero > Instalar modelo Jockey.


Armas 3D: modelos de Assets/Armas conectados a pistola, rifle, escopeta y plasma en manos y recogibles. El rifle de riel reutiliza el rifle a mayor longitud. Prefabs normalizados: Assets/LunaCero/Resources/LunarWeapons. Menu: Luna Cero > Instalar modelos de armas.

