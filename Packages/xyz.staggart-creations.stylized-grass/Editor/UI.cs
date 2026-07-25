using System;
using System.Collections;
using System.Collections.Generic;
using sc.stylizedgrass.runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor.AnimatedValues;
using UnityEditor.Rendering;
using UnityEngine.Rendering;
#if URP
using UnityEngine.Rendering.Universal;
#endif

namespace sc.stylizedgrass.editor
{
    public class UI : Editor
    {
        private const string AssetIconData = "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAgAElEQVR4Ae2bebBlV3Xe15nuufO9776553nS0FJLAkmWMEYgJmOMgSLBOOXCCRBIMCapInGBK9hVtomp2IRKUpCyTYiZbOIQHEAYsASKEEhogJ6kbrV6eN1vfnceznzyW+ep21IjCUkFyR/J7t597z3DPnuv4VvfWvu08b1HPyfP1gL/sAz9RcmXbpc0Wc4utazpZ7wlDld/7NxSq/tjx3ZvfX127OjJf3Xp3OTUPxLfO33pt36ZHH/RU37rj3Bwj5Tze55yvDWYE9sSqTdu/Pvjo/uz7/O9Fdk+9Yt/f/xJ38wnff9/8uv/F8D/ZbXXef7M85yD8zyvf9bL7Wc9+/xP6oIS+o87/WVjnT73R5Jz976nVLphjlOfLpb2X3bF+s9w9Mjlx1/GgUn6Z+jp5Sef7+8XJIDx2tVP+5xu/+iLLXEVxbpGGj/tNU8cvInP67zRvYcaY+/8Mz0WylA/VLvasx96oD3sSjUX6teLbZsfnnuVW7j5f3CgV7Yevnj8BX3+NDFgJvSP3Vqt3ni6mN/6lMlsmWyIbZcv9cHgW5+s5B78uCGlbqv1Fwt9/6R0O0eIAPM7TSO9iS6GkVzqqXtQLvaBd/RXO8Pzwdn5j/YalcpTnvNCfrwgC1hufkNck5jz1PYmFDfeav5FWCnfdvHk05nBq3LWhSvDaDyJ0w13z0xeiZY7DYYKR8Nj/9hxqlnscnLjTx19/df2Vufrt9aqv/hHT5y8lk91t1NP/H7eHy/YAkqVl4j2nLtbwuiCdLp/8w43t/tuunTa90zF0bBKF+3dQUcMczrrzdY33u0gHi8qnjXtMXWXmSAMrjCsWUuSR98dBOe25pzdupDipdWEhxFP1t8y8lelkL/ybzdP3yqj4f2/kdoHA7peuoGev3TPc/zyfASQe5YxPxHFrQ3Lq5/8bBisYrpWMYiWYrrk3aeA/IznH3ut6zjS9fMPOXZ6izf6zieiqDM+Gn7395rdHxR4hmLA3pWV7++hi/ax+kskNSak3b3nbabVkLPzv/9Nrnl3FF1oBN7fzXVGK5Ivbhx33MavcDyTHp/PqT1XF7gqFUvRWunXkctGvmVp+WPvmBj/zd994riVs8tVW+rnnnydAYscjB56hx+2zaGfI1SMn4+j1qQX6+JPfiQMFncNfZicUaxG0eO/PNm4+gd6/+YNt18c5pZm+64rqtVX/t3VO14ng95d73fKb/pnejKNMk8b97ylq9K4r3T1pGMWJbp457N8PhcL2GUa8l4jHZlpmmyli/Yn2uSF+Q98w7brUi3f9nm6+KFZCaJRL4p6Yd729LLN9JvppW7/+JvzTl66Q1zCqC0mSWcmiOJKq3t6q+XsulPE5Hgu7vUPX1uvTe2j6/3aruj3739ft9+TWvVlf87v3zXMnBkNv3wHXTxvXvuGfv/IdBgsFejZTc/ln59kATlJ43emaVCRJHHDsJkhUxJ7cv78R/H9s3eEwbF8qfyWby83/8vx1LxWim5lNkrmnxjX2R7HK1NBIrvnm0dfPvIWDkzXy/i/0zEk3+4P7n2JH65Mm87O7+bdXQ96wdduSwkACHBsee3IJl3AcLimHy+dW/zCG/OFbWnO6G/qNj/3IaPw1g/0RzzGqEneXtFrrFb32OYorm/RH9s2bleXVTAe6e9navbK6see9lwUd2V2+veno3hQM6z6kmGmNctyM3RXAdD+axydOJQaDXHzV31FD3iBWFG0UsrZ9tAyHUmtqcUo8UsD79GNy82v/ZOpqmG6br3np+P35d2ceeT8sWmTkH/t7jd8axhYzQjDisK+7QWhEcdRWcdca5/Qjy39UVtmxq9cLJgPvcvH4nvt8+MFd5vGwd5atyPFXOibZjnf7C9vXW6fk5nGlTtMCfZx/ks6wDO1Z3OBjy8s/facH84dzNmTpyQZbU+iXq3fb+tY/244/MrbLHsMZJ9Ka8VrvzAchhiJ70ZSisSYUjAD8OZHllWpSbJ6oyFppZSvB0HYqTj21GOe3x4vu4nUSwUxzeJiKnEhQQCWaefFsHOmxDW67N76HhkF0eYwNtF0f9axOlsT85b/MBge/rnR6HHFJAXNXb3hhfO2U++1By0NjS8e+AvN1MzttYsZhuzl2NO2ZxLAi0rFm3bRBXS/Apdve6OTB0feQB/2pl7vc7/lultIk9dkrHLT37a7X1TAK+Ts9LaclSs7pmvm7WKdLt3+XMHzT99mSIAFldaGXocFV8/2R0ubGpUyAihJp39+ZxytblNiG0ZhyTSt/HL78RxdJ50fesdfZhpFBBARdg9+KTUOfLHTu/fnUjGrebtzveMkr3Hz+xqAanmte/8v5IvbpkvFHZGRtpVf50qO+w/5NHSwy5tdb/zry4/J2spvf960t/+hnggGPyjlnGnPD4fmSuev/6BWrIyKuQZ+zOMNU8qFQ5/W67zgwqFiLv8a0zD+UtLWSmrM9PV4mhzOjbxzxXwOl0i9+ii0pGxNzsXRj149wJQLeJXlrBwKwthIDUuGQc8ZeF2nUqhlMX1x5e63jkYnpydr2yVJB1Iuv/oPhnE7HHgtWev84H2l3Jw98p0zzV5uWLTb9bwbW81etDMOOqO827tmtfXNd0hiLK51vn9zakzeo3N6cns6Cyh2Bme3l4s7z1QKL5aR38PMq1ac9K92zLa4tlMIYkviuC+V4oF+lDb+aqUTQHPNGyAxkJEU3Fh5d2947BY6oLhxPEkDKeZLnOoUTGPyXJra/ZF/4TovssWy8ljZ8t5mf2FPlBhSKYxH+Vw1iZOgvGnq1+X43Jd+Y7w6I2NlS0qF/adPnPnD+1bahw/0vAiB9HcilKsWVh/YFsW9CcuuDUpOIkF4+vpRcOwWz3vkYKt9xz8Vw4qXm3eoQhUYn9LsxeX/+JQDldLtrxoEfYkisw9jKw4DkTMLn3mXJQvX1Eo7JU5NtO+JLX1Izr6v+/5dZCr7inHsz9qGnwc0d4y8468IIxl3Cm//Qrt7500GETlOUoTpIczN5+ZX732pHy6V45Tk0ShIf7g42x144tglXMC34yTKBUiAiZXieOHmmcYuubD6gGzb9M5P6mSPnX1o0pAyAkjqrt3bWCvW/EFY/54f9IcztYIMo7W3JIn5qVSKpwo5r97qHr+lM1gd2zq7pTD0j+q4l9rlYfB1i81v/FaY1GW1da7ih6cOxSBTFD74ElgWN5UApIHYpi3Yq+ScXV/XkfywuieIjm9Mk7MHCHmjIDiz37IPKHHat9z69uuL+YY013wxC1hOurwP7e8So5Q5pU3SI0YuK2eZRiStYTPXGawY26YOWGu9E29olE1cDRAaVXwv3akcQGqFx4srOcKpv7hx5CfMp1Bfbh9+Zd4elLdPNaSSW7ZqpdljhtEYFHLfee9jC3f+WsHd9cWBNzdmgMYMoflD1mz7SfW9wfDOj7R76f7uyJLzzROv2tSYuVDMeZjxNOjsSBD5aDKRnBXiu9s929r3VyeXDkvJDabTZDAWx49vctIEi4jR5OnrTsz/8SclWdxQrk+wwC4TdSQMexNBGGL2jmwcz6F1A/IjnCcg+pFMOIVcrbihfX75vpf58bGtr7h6rxw5d0zCdOt9UxVjaQSL7DtyKEmwqlQFakV+2GwM/Nb1hjF1WJ9xxaZd4kfFlh+c/QXHaEnOrg/KxRvuWut8/T31/Am3WHz7ey8K4MkYsMMwk/1xmpMmIa09Wtpjyg/f5GhUQuMRDwyiMBOEafTEdW/46mB0h8bEnaOoP4PfT6c4BnA4oZEJkxsfDh++tZyvcD/uT32gOxygbVuCBAuCUZTztpxbxfRZfAkwDGKCYRy6pmmHJxfP2BOVdG+1MCbHLyzBaFZuYfAx+ngU3P/GGAJkWg4L9e1GxTEr+bx7obmyJ2ebqePU5c7Dn31XHD16G2GVSHXVnXFi1fL2Y++UdOXXGeNSRLDj6JJL3GgxK4fS6iCs8JST14ehQegaA8nRaBwjhFQKloFJ5rgm/zcMhCt0rk8jc0uctjc4loXmg3LCYi2umaxNY28pEcJDCznZs2GXPDz3uDQHgezfUMZ1Ells+wCfRQw1sTSX1LhtgheYaU6u2rJP5tYWZKU3lGu2Fw3HiFpnmw/8Tqt3WlwiUdGF5gWhmFx9cmUoI29U1TnMt/tY1NqLpmuT0h4A4oaThOF331pyupXE2NN3cpvUDbJmRslOFrYTzRZ2aDWLMIYQTJkoe1MwM1cXE7D4jP9zG8BDRwBeazWID+r1G3rDlUqaDhpqAVEcGSHJiZq2jgOrQ6u+/PwVN0q1NCEnl1pozpZG0Za1fkiWZ0hnGOEehFTXFNWkbbnhoR3bZLziyuFzp+EKYyx2osWMf3V+9TtvD1Nl5Pg+CiuB1D4upWA9IKp4kSH1giHXbtlCGh5hdYoRj92yZdzc7YWudEdDDc8KaFm7CIJlwywnBrZqobFtdRutEJ5A/LyN32L61Ge4ARPNrAGN5qZgAjK71n3k0CAY5SeKHvrI45NqKTEasPhMpO8N5eD2a2R2bFo+dddXca9EDm3l0jRCO4E4ZFoDkLMXRFwbyKFNM9OEw+UN1RTrWJFTy6uyY3ob13XGjp357G8OfLM/WyvL2qiJ2xRk4A9IrUNxDEdDNJZqycZGkVDoyEOnL8h4NUCA+XHL8mW1P3XUtice6fc/+6fL7YfeoBIw+yGgEubqzKWUMCnVXAlzDPHHBM0XcgV8tUgIU+DiGNY55Ds8e9xJ5/f3h6vTRae91QYYHTQbxVgIwK5uNgoHMlWflQObrpL7Th5B+0vE8qKMuam0gAPXISxjUUqq5jse2q9Kwelunih39hXcvDyycEF6oHwJ17hqSwNLLJ3CffqNSkH2TruoQ+To/AChRhIysYMzkzJWArMQxnLPS3uen1aLeeZF9PJ6Uq/e+g3LdJM4vO+XuXUbXczeaE3oO1e7R17nWNXMBTQpWV9ECvAFmKwpfT/IjqUaz9EYitvSGy1c7dhRrVHyN4ZxjoWEmclbmPMAzdcKJblpz3Wy3G7KfY89CgV2ZGfDxqdHkitslKlaFdPFTAcD2TA2Q1SYRdNhuVZIpudbHZlbbWPK2+SKjVMIviDfOfrNlzjG0sHJSh3sSeR8K5YOmVF7gCvhSy890JA8xRZV0txKl1BaM8quhm7lHzXIWLFpmxeuCuOSNHuj6+litvrHhf7yOJm/Ik1VeujuEkYamfnb1P904SHaNRQjYG9sl+3reoMJw2juNKS5wTTdzDL8SMNTlGHGzul9Ui0W5cjcERlRGlOSUs/F4qcV2TMzBQb05WyzJ9snp2T7xKScWlnlek3wTDm1tCILnb5spaB6cOus3P3IObBiccM128bySRrKiYWRnF71pVYqipHAvzwJxioEedy2R2Wl4MDEwJBhgEtm867Gy2uff3/Rae5bbEcIJd5PF3OqdIaJPPRmE2IfquoxXQ1LCQtWSahPuwANeMgEsAaOWcCukQZl1yoFeSeEXFDI4E4Fv2qhhv+Fsnl8u8zUN6DFU7LSWWRCLj5qyHzXZ/EbZUPNysLblvFxuf3g1fLfH36E0BvKWBFm6Plyfg0kR/BeEEjPGyHcthzEDTR5GvCbmaZ5xxB1h6JRTOuOmZhEMEIoi+vJbAM8wxLFcFkGeYgEoNKwDkPNlIhrbFf3UB7A+ixKLwQsTEe1rOitAObYNr+Rlu/jy7XsWMw1MRoYhf2KYa7NxNHI9AIsBLZmMeEyvjtJJWdjYyuu1ZITF04z+VjWhgZuFGGieblxx4wcPY9QcsX0X7zmVnnw9Bn53qn5TPuKQUGkGGTKeEnBmGlDoWdqttSIexHH1aQVrq+cLci+SZe5OdahXeVcGIMLuMbAH3K/qlL/kGwxLxTMnGMsIpTpOjhUys8qXpgwqppBdC/m6p0Uzj4kydCLbeK90l1KVtIZjWSW5KyYQ7osIoHs+VFuxPUInfQfCwgQrfJ90j7Z0NhEchSD4Gdkod2W811TFvsRE4tlc6PK2B6TiuX9r7nFOLm4BGl5RHaMlzNQtNgTMNGchsc61jBTr2STVg6CbWZa7TKfHBNslCFCYMiWWdfcu7VhoifwpAkGqFZtVKoCULyCdSEYqltZFEOHCDWo0cX0ol5usrrr4ena3u96PrU6TMJjUB0gBvUHmOXQI/nBKhrlEufwG8zMNktrtjR3K/GAfTFgiLBStMQmCGA3GK3KycVFObGayIllwDFkOkxk62QN0PLk6q2TaubpHQ8fTxvlokyymG3jBa6LcBdTVsmVS8USQlAWqq6p4ObwLBaJAGokl2otHoBczrsoRxOpAMV4uJqt1sU9lFRQlgpUeYMKJiKyqVUDlLaCpd0LNB09ezWZlBmlRaS2fqGGNNU+B9CcmlUgM2NV/HOEmSkInrghZw+nclYhuwcCJFsmZ8kTNLT5MLglObHUT8/24OsYSR/Lum5LAUtiYmYeyxjIH3/piH9wh52L0tiYrFoyWYFgYaL60Fq+INNVjfM+YbCC+6y7qEXC1B2Rn+SoLKNRaBNGnsMtwSuQUEXl2rgCX9Qy1V00ebOsdRfVnENxrEB9Qps58NO4NehCX/vVQaDhbp2VZT6DVisFR1ZgbOdB6zo+qNL2Ap3Mwt40HTZUqwpUE9UJQtkEEveZdFtWum1ivZUOkNeA63mm7JosZhPVibep8G6fyplFKgRKnGZrLvrC9wmlmnO8ZE8DDMCSELz6tfISzR1a5BNKxiq4moKuMkgLAqRN03gfC1KNq+B0i02bju+HI0IhUQ2M0/H5nuhvc0O91B1FzkjDx4DFt0dICinpQ9TyVFpFTG2VmKmmOF7B9miOaWOohq2DKVGaqU9hvpClxCMRaskZUHyuDUpEhrFGnL5itgz/r4LoijOjTLA3HyiouRhjBTtzuYw9qrkgIE2DsQyeRDWBY1UUgadlFqiJk1p1jGDUrPUa0yCB6w/QOJyA35q7KFhr3qKWPIQx8ihZaPW5JlDW2VfmaV5oR4NGKbeqADbXDrhJMzMbM2MiPEQjY56lDkYBGh1lcb2E9BWUNCKoBsbK9fUJYYLNQZuHaZiqwtBiKHOK5Rjyin1jsDWtHfjcC4giLJswtn/TGDSZGirP1fCrgidyM0nNDHVpCJFjVWSVZvyCVJz5rJM1zVG4FUFoqr7c1aTLySxJfd4LhghHTV6FpIWKCOUqLKay3Bkt0cXcO5sX+nnN2k6uEO6qLhcmCMDnwhitkv9jAXtnyQp5kIKIWoESIzXr8SqsjD8+Eg4RwEKzBRCZ8sbrrpIDMxXjQmdovOmahuyfLZL6dmGLPqg+zLQyyb2VvIPb+eQcCqxKZNaRul7ZyHPzjLleg7BNKDGCX+kmcnRuAF3PVsV5xYxQmn2YEBbaILRplmmQdiufME2fccllMoxALVhSs48S4vSsUnuyQRaVJCe6IxaGz42Bxgukk9NV5dBjsLH1CDBZLTEImkFz+VxeKiXS3WqVtFe5QoD7DDHvIdrHihJLtk3lZdd0zdhELe8VB8ZlEQK0gsQVRPU6k+ihNHu11+P+FDDTRIZoEY0IdRRKAFMP9qimjI55dgrqW5g5xExrFPi3mr8qRF1EcWUG5UUsVAVahYazPwFpCuU7J5sIBbdF+6q4Zk8FIo/q/WbOSkHu9IcKKC/fUybm+0gyL1du3g3r0okgXyaIdTJYhFn5aCnFUti3QAnQYcDIIyPD/zBvZZPqTs1+INdsLsg/uGGCy0w5PN+Vc7hYD1fCErMxljsd8IKZQVRczFqFEyFMxy5kJt0bdRlrfSsun3MRcizbp/NyaGctc1GF+kRdhvkYYEC95JIwwUmg4xYWoy718LlQ7j7ZBvURIC6nEU0t3I/Sh+jwAAal36MP3jhGooLGZ+t1Cg4UF/0mJqVCwI8QQBdO0AYHFIyUXa32FHSUqnpZaquhzgdlLXxDwW4n+yN7Zqpgh497jWRhoIKBraSEWNBauUOXyTQ5nhVmWJCCrgNb9IIRk1VrUgGwSOXz/FFarr6sPq34p0DYoejRJzSqNY5GcD/GWOr2iBihrHaTtEz+oU1J1gIFGNbNNJIjdJgg2qE/aNu51ZUeW1H9RLaMmXJuZZHqjGqD2gxWoARFsUB5wrqEqR7hszqZZcriTYoPEVRzpQviUpTQh8E1eCzSQjvQJxhhJHNaQidjC9FSgK+eaYVyajFMXGWeTFzDqpr/CJfqYh0hs/UIYRphlJvoqjNixKdaogpoEbaphRpNq7skQmVqGeqKXUDYk9CYhEaXYVAr3ZB8huhmyF14isKCmDvItp7oXzu6oHsAFgWIBtL30YLSXK7kDvVFXDCjwx6+q1op47ddNL3QUVRPyd2V86tPqkkq8VAigoXgDi5WFMRQbEpjSlAWu0NhU0HuO+3FG6tuMlHT+p6yNqW7TJZMcamHdvHhEaCpNckSqa3uI2iyxSBIgLCSCYIaBpGr2Y3hHH46M8YONNptgmux4UHA2NvlujPUH5XfzHfi/0nXT7Hbuim/3j5z5Hz/126/ciuDowWyt40NzcMBIvBBtZ4JAzNUslFyywCNJhcpk42kXmYCXkxxRRF43W8bLOq/3T1KoCxSG7PMTRWHyYyjzVTOrWkhI5WZgpP+yvXjVjsTHPG+pG+hEoUAyvOdhLdBRjIFsuedCnPCxEF21sIVLD5Zz0IVnyK+K1ESmKKPa7URgFa0Nlci2TddktO4IHsNsuKl8ngz/Osn1iz2lx+4tJf+9apbWL1xV2NCzTlnufg/1R+4fyvsU85GANzVgsdXIT5ltyg+IaaCaR1fGch1RJAcM1nztIID/QWc8vgOUdHYOE5Sxaw1KT20uSp3n+rK2ZYvRctO3/riMctxTWMJgVRYQB5ObyTwCDR+fDUmkijnpzSXZyOEMUe4g5q+SkHNXr8rJxnAW2YbtuESVlcIiQXMtQ6ZGjppZvZt8ACDwOWiL5FHdS4J4NXXzl78rp8fx6o/3EVKBULdCGRXnzR4SOb3+OlSx8cMtXK0jsAVkpW5diJvrLsZyluEty1jTpZ6uljE/m15o1bw5RH8r0ypTbV0cnWAAEJ5y7V1YzulrceWlbBAwSld2fh/Km0whWImGHLFtMMClSmC8JTYwjDAxahOEOJUAFAhBIPLISSl8TX8vwl2FbGWLYD6HPa30vNwo5Q6IrXHfvqRJy/YPrRFS+2X2p8s9UYfzpFM2DAqC0KjLErBQs1Ow5DW367ZWsJfoZP8ZntWXrqdbA6298CZSG7eXCaqQV/RVpdFHNheALxS+dGyL9NlkqBeJHedaJH+luTFO2ry6GIflyAMkqwUSHoUH9rDkYwXTHnzQcZiiXECDUawy+0ez13PVdar008sngnqNnuEQBzG6SP4PNGCLQK6TYQayhKRptVPjkzXzO9dWi1f7HnKTk9qumX0UdLdf6lWpuRDQ4/uAvtIPjUqMMIaVFRdI8jcQ+P37fs1a4vlRVty1PVcwJBNNFfBkKIq4KTJSYILzUCcjlPKWmr68sFXbsw09kNY3S58FKWSH1Qz1+mQ8MzwdoAN6i+2R7KPDFL3FVd7HUxbLQTNA8KpgjO8I8dii5iu1heVg+jCGxRTOoRGLeau9IlIKKHfkUs7QhfXbHsMfFn7YMGVfw4tddXn1K8V/AIlOAjj6q04ZdLNBldkzkIWwNcDrSdIZ3WxMQtPNH7rjgXJap+XVHZOW7JtupAJ4HX7y3Jg1pU/u2cRw8e0AdlymdIWuOJBpgI4AEPiBprMkJuQgTbJHnNYlpIepboFXFOVpNldrahCSTMy1UP7WtWukmAtNj250NZ0GP5gGt+9Zo9552VrFbta3nj5MbhM8i4IyZ/r9NXXXKQ+MPyM8CguNAA85Qs+cd9lO1pDjMZrTaRUUB7WQiqfaUoxQbe9NUo4aGoGVL7y+gbhz5d7T43ktVfXWAQFDhIq5QzK/dXMtdChwNZgICVHQUgIU/fQvQQAjkhL4x/+6nF2lOlaE2D/DEaoeUuXbE8FoqW9o4vh2/SOy5tdZHPhadqnQNe38+BbFfo1w0JJDKrcoC8mZWk1vRFm71L6UkdRVqFlNLUE9bsMO5CMxRfN821whRjFdYBhoSKf/l9tYZMIxCcWA1iT5UrGNUJyAd0k7QHZEYLIs3egnE/vU81rpxyWAaGCM+vkucRBrvFQiFqFScFF6V1HU3uef64b/85kyTz9NOtU6+COp2+/hGkup6bhFGBuA/xLd140e9N7ziz4hJWAaLFeOsMAMnMcIPE6/qdhUEtnWo9TbRXY3FCX0rmSbsjccpiWOdcbBMZ1OxuYMbs5LNhHAGugtvILDXH6YoWGRC136dhKZHSRqgAVsiooS4yUFvAgdUvFk0hPZlaQ3F/Pye89/RIR3tmVs890Tnd+X+0l4Ten64AT5q21v1p5jMyNguc8AEN2uHFS9w6VdKxHCeXXFF0zbbpgQFYzwA34SyiinoD2V4Bah5kXgZcivrp/cx3k1sUwFiWsHqY/gk5PlDXj1DI4lR4UoGOrb7NUhIV1sUatVSrYsuOWbeDkNGnhPJUuZbL9TpDezoFnbLbPpJ6lfYtz70Xi/34z+205ED9hTx5X5pO9O6ik7usNYXbqBl00pBPLUlTMXzevNIXWSDAAjbWgupnq7+FzTdzAlEkspQrAAbVoWRObEdZBtoi7aeFyy8RYlvwsUsXpQ7CU3k5U1nFGsQYYzMZW01dtuxCvHFRe8WDIvEiGXrqhbKkin7HZM9T7f0L7OHnAFLXBD2rOrYvj9RNED/dH8yvEda3mkCcRv5WgoGkmpFFANaEfur/YIo+PFO3hC3NwcpuaexlJbp0qcz0SRQwRabXSbE2SJgDN6bF6ljGGpOAdivCab+j4wEwW8sapJuf0veOhvl7DwhFQSoh2EUCrH75292zjgZ+wNrG15v8c2of0xYY4ij6oi9I3M/RlhgH9fCsBwJg+SL1GKVt3fEZMVP0d9WeWYXKvFjkUJLXuOIeBTugAAAO6SURBVFnNydxcTyKrKLs2laXby2CUMSP8HxLE4nWH14GOdwZdXCeSJfINDWeKARdDYTmvFSPNR6hVIkzlIkqL3Zz9S2Ml46vPYV3oESB7ju1D1P9Wqcv9SZrAFHEHNcNlFq2gmMcENB1Va9QdXU4pxGfWocmUVpep+sn82hB67MipM5Zct083Odl2Az8siw0Yag1aO5jC7+slWCCG5pMJrvbI3NjPU4qdYRvuxprRulAHJGXWARA1BY7BMAxf2xmG336OaxJ7sj7zXK/V6z4GMj9GaPmsw/9sUCUPWWk/hKygAc3QEoCv2WIXaBzLwLtihKKsrUAdnxc9wIsQ5FfO7soNV9Sk1YVrslB9KUxLcYrgCralQoHFE/pIy0+z+GVo9Saogvp9qiQLcSredCjSaBLYGcUP8fuNSP3081mQPctW1fNsX+H6/RUn96dhkL5Ki6bXsmnpURWapyprQTouLEfSnIhlz1ZetMQdiE/iwM4UG0yI03EIUKPakAbxabXJlAFKDYEtSI46Q1F3elzeWxj25ALJ16MrcA5qCQPGUlKkZq45g1ad1QopzHwMN3if0vbn2zRmvJA2/4UHL7wa2vEu3oxenaCQukq6qbQZpUizo0DGF+ajMVuTKaWsWiZrdQPZvNGVWw/VuU4XormG1vQ9kD7QDQuQnaKnajr15PBcQBKEWTCwhja+AcTcx7jUKh+CA7x8GCbvo7+QdWBHL7Bdt7sq9E+QDe5mZ/rfLvWCkU5eY/XiIlSWWK4YoDCofhtiBaq5PlrbtbHOm1vsLYDmGr60gNLsUwJDiLxFzhuo7BKZEUUMXx54nBIaFFfdTTFGU2FA9gxv1r57sTU8xAjfeoFLyG57wQJ40kPbf/mDxQ8M/WSnn6T/ZrUbnu40c1hDgQr0+osSagUZP0EYWtqs8ZqMUtosS+Q1WjX/ZQBCwWyGzM8FA4BTufdRT5aJMhRzM44Pmfo+Z96x1ol2eV74n+hPmsYL+/rTEMDFJy/w5cP3f9XdkRP39SU39yloKe8FqymD2CxCMcClvF3OaPH6o7W2qGks7/Sw6+Swv6hbbzFbWL4cPedT0TV/FEXJR8MwvfnYmfBGTv7n7AL++Wk08PNn0r68a+/oy7z6Q76S3IAEbqAKfPVYydlTK1Q2QZImTDNXhE1SN4VQkf20e0Hvii3V5WLOOWdb6SPnloOHFleD+5jdI7wW/DNrPysBXJwwuhddhPasKXvzo7jkOGYZ5me7diJz7KIQQHuHz7W967dTb/g/2P43Lr/vAxmruRsAAAAASUVORK5CYII=";
        private static Texture m_AssetIcon;
        public static Texture AssetIcon
        {
            get
            {
                if(m_AssetIcon == null) m_AssetIcon = CreateIcon(AssetIconData);
                return m_AssetIcon;
            }
        }
        
        public static Texture CreateIcon(string data)
        {
            byte[] bytes = System.Convert.FromBase64String(data);

            Texture2D icon = new Texture2D(32, 32, TextureFormat.RGBA32, false, false);
            icon.LoadImage(bytes, true);
            return icon;
        }
        
        private const string WarningIconData = "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";
        public static Texture WarningIcon;
        private const string ErrorIconData = "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAD9klEQVR4Ae2bz2vUQBTHJ91S7FZ6EH/VY/GuRy8evHoVWnELgl68eBG8eNhfUPoXeOxB6B5WsGAPpZT+ISK46j/Q7MZiy2q75u04a35MkjeTl7fbrQMhmTcv8973k8nM0E2dQbslrIrj6G9rb+ntVNbVNbueBgPtfTNa6wUy/gdA/LCrfn8w1oo8IAZZoRwBVdHeapBlltSRjEEGgQoAj3gFhRACBQBe8cQQ8gIYj3hCCHkAjFc8EQRbADWWCU+JzDrnmBNsANTE+1Y9Kyf2dksIs4aJ2ou//6AlHq/hlq+PH16L/d0XhrmJ4aiUW+Um9l6TEWAvHrIplz2xs93xr9KPg72OWFjoYgXE/AxHAhZAPvHBLHe2g7Xw9cFeuG5bM4CAAUAnXgnSQaASr2IgIWQBoBevEgxCoBavYiAgpAEoTrxKECAUJV7FyICQBKB48SpBjrOEUNOF0gGYLvFKtdy7xCBEAUyn+BQIs/6uTjXDuR6sTOW1HAmjv1uY7gTtmZydRUeb7OvkRNfnZZ2xCBsfgB/eVQMBSwa+uVz5AHS7NwwyNfE16Dbuygeg55qIMvGNqzKw8AFw3SWxGZpwZZq6H1iePWF7BfQTkwFBtOvJMUxsZYT/vJC+CNf8LnwjAHJ1D+HJfslI+1ZGO2kzLwCvdx0BAHzYCi8AOQKyxLG9/5AIL4AuaiVgWwH4AfRQe4EpBuC6N7PGv9+O8UF0g3PhfQW8LmaCw/jg1CG8eAEcHmKWOIwPQhrOhReA17sWSqu6LquNN0Ez6whwBo8eBoPDhw3Flbm5Y387LHeDnheOs7go688rP0W/Px9uJK+NPnDi2wqDBinsr1KtqEUG8aHAvK8AhIaVoFSKPH7fDiPi9JR1BYB0+AF8/3pHLN/+DcFj5VvnbsxWsIF3DihYjEH3Y5oDDDLkcuWdBLlUGcS58ABmxUolyKs+kV9/BDPMe71SqQe7iK4CjSGQIj+Bede+Ekwgdr1e2xWfP92L2SkMUvzoRxHoUvcKAIQ6RTxtH09XXd+uPzaarpgp6ZdIbWcGRo14uFsHAOxFQ4AY4bLRDNcpa6trNeE4Df8Q0SMJAITng1C0eCES6aYBKBbC200hjo7k8fIV5fP+1xc8+RTx4JgFAHzoRwKIjxZqCAjxkAIGAPjRQdCJhwhQqCAgxUPI6DIItqQCEATBPmE5KcDQ/qt/KbU9q9FAPHTlWPzT1OR+RWIoHgBgXwHwVYXudVA9UpwtxEPYmei6iKw3hAxIkXr+PizFSwD24ZsTASGH+LwA4P7xQsgpngLA+CAQiKcCwA+BSDwlAD4IhOIhaZONEPhnlabvAMe5KTb7gHMjDpPohQfwB63ZMyXk5VWPAAAAAElFTkSuQmCC";
        public static Texture ErrorIcon;
        private const string InfoIconData = "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAF4UlEQVR4Ae2aTWicRRjHdzebzSbZUKiKB0FEGnsSvVhREASp1puKRaH1AxUEL35eREm2KQgqgnjTUxU8FLWRFBX8qEEreNKL4MmDIIgULFuSbLLZzfp7Xt+J887Odt9935nZrTowmZnnnY/n/59nnvnYFDc3Nwsuw9bWlsvuevqamprqkeURlPI0/je0/Z8Ax7O4QH9dz1HGcBZcWsAC6/+YM836dBSP4YwEVwQEAa84cUmCCwKCgndNQl4CRgLeJQl5CBgpeFckZCVgMYTDUyAHpXl8QhYCBHx9kFKhv2clYVgCxhK8IjsLCWXVOEXqDXypVPqtUqm8NTExcRY9Njudzk2tVuuFnZ2d+RR6JaoICfF9YSnxoU+hmPIy5A18uVxenZ6evgf9GgCO1IQQSWsbGxtnhIxIOOQfSFikyUAS0hDgDXyxWDxXq9X2o+h5wacIkHwcbltfX/9GFYZN05AwaAl4Ay9gJicnT2CyEfi4XGAZ6Di/o9AiVnRh2nya5RDZWp8OvYKXMQH7kwBWUSwApSN1JE+UNfH3uoikw/8REmjV9+7QjwDv4AVKt9utEiWNogXe9ciqFvlQopgE8Qk9wUZAEPCiCQ7uLvxAod1u9yjGLiDW8FLPh4wC+qrTtIcEk4Bg4AXH9vb2vSRHJW8E8U2v8v0BQ56raCOh2Gg09E7lMSN06LIVniZ+ijVcYNnPA/wI6XUeFSmqvseBAKVLyHSXgEHboE+ldjjw/CJnAZzgHDO+j8GmfQ5o6zs0AWLuX7H/v0+6jEINTnuF2dlZ0a3GGj2O83vGpqgvWTAC2Ot/5GT2HEBWZduzhDWs4FmI2c+ucLfluxeRuQv4GKQL8OPM8gGAr8qhB5CJcWQblIhlSPww8dFzwTcBbS46RwC9APgrwPI4cU7HNDMzU5CzgBb/1L/7zienwu1oHcAfBdhJuj24trb2ASTsAfA5yiv6UJi+XrxKL/jOeyMA8E/g5U8C+o5ms/kx6QxgmljDWR2UzLweqHurXvad90JA/LhxAtA3A2glBi/r+xTvDwkThygd4wS+4JAu8J33QUATp/cioPexxZ1W4AUIxLxtAhLnp4U7qb9XK3vP+iCgyn7+CsfZ+wEjji8KLIefsYZvVVnSeP/fFWEdD+8WAmV8EFDkMPO0qT+z/44pM8p7IE2exoIGHwTYADRZ/++ZHwCsix6kkPvur3eYJh+EAMB/ZDo//ETiQMTyCG7+QlAQAlKY/zyPI0G3P2Ud3gmwOT9egtX4UYrTfCQhCFjwTkCK2S/hCx4KiDkxlG8CrM7P2Ptv5yh8dUKrgAWvBNhOftVqNXoGVxhxjo+q/ChSr7dBCHiXqK65UWqAnMMa7tNlXJVXIOkpXeYz780CuOT8DpgzpvLG3n+Y02L0HCT1qP8Ft8XD7Ag3mO18lb0RAJhP2Ns7uuJi/sh3RdwVHlMFbonfczGSk2CHZfGmkvtO/9HG8UgA/WxAl3uZ6VukjoBn5g9xhN7AIt4IeSYoy4lMC/X4xwNNlCkrj59fmy0Bp4vkZaiNj1jGMp5k3AbL5mVIkHdDbwG8db1z28/juX8d4vDzK4NcowaSZy959ZHIuUCJJa1ASgvgosfrgH9e/+g6H4M/pvdr2wXkPyzqeqVh8+D5gxi98xkPHgV+75ffBAvyHM4OID99X0b+1CjACy4bASLPRQKzerksLd3hSaeWcBBCfoAIr9dgdFlk7MTMK136ESDfM5OAqV9L+4s9bd3I92Vm/nPfp8AY/JIAsgWbDzDrZfIJLIF11vtrWMGX5M8D9EriAXn0UN7fHMh1eRB4GS8NAVIvEwnScFQhDXjR7WJLQNc983LQOwmVTwte9ElLgNS9JEgYBvywBIw9CcOCz0LA2JKQBXxWAhQJsreORcgKXpQfxgeYYJfigU150HIe8KJoHgKk/UhJyAveBQEjI8EFeFcEBCfBFXiXBAQjwSV4UTrtUVjqpgo8bKSql7USBGRtam2X1wlaO72UhP95Av4CjZqRyGFGUGEAAAAASUVORK5CYII=";
        public static Texture InfoIcon;

        public static readonly Color RedColor = new Color(1f, 0.31f, 0.34f);
        public static readonly Color OrangeColor= new Color(1f, 0.68f, 0f);
        public static readonly Color GreenColor = new Color(0.33f, 1f, 0f);
        public static readonly Color LightBlueColor = new Color(0.4f, 0.6f, 1f); 
        public static readonly Color LighterBlueColor = new Color(0.6f, 0.8f, 1f); 

        public new static void DrawHeader()
        {
            Rect rect = EditorGUILayout.GetControlRect();
            rect.x -= 20f;
            
            //Draw title
            GUIContent textContent = new GUIContent($"{AssetInfo.ASSET_NAME}");
            Vector2 titleSize = EditorStyles.boldLabel.CalcSize(textContent);
        
            // Calculate centered position
            float totalWidth = titleSize.x;
            Rect textRect = new Rect(rect.x + (rect.width - totalWidth) * 0.5f, rect.y, titleSize.x, titleSize.y);
            GUI.Label(textRect, textContent, EditorStyles.boldLabel);
        
            //Version
            GUIContent version = new GUIContent(AssetInfo.INSTALLED_VERSION);
            float versionWidth = EditorStyles.miniLabel.CalcSize(version).x;

            Rect versionRect = new Rect(textRect.x + titleSize.x + 1f, rect.y+1f, versionWidth, titleSize.y);
            GUI.Label(versionRect, version, EditorStyles.miniLabel);

            //Help button aligned to the right
            GUIContent helpContent = new GUIContent(EditorGUIUtility.IconContent("_Help").image, "Open asset window");
            Vector2 helpButtonSize = new Vector2(37f, 22f);
            Rect helpRect = new Rect(rect.xMax - (helpButtonSize.x * 0.5f) - 4f, rect.y + (titleSize.y - helpButtonSize.y) * 0.5f, helpButtonSize.x, helpButtonSize.y);
            
            EditorGUIUtility.AddCursorRect(helpRect, MouseCursor.Link);
            
            if (GUI.Button(helpRect, helpContent))
            {
                HelpWindow.ShowWindow();
            }
            
            if (AssetInfo.VersionChecking.UPDATE_AVAILABLE)
            {
                GUIContent update = new GUIContent($" Update to {AssetInfo.VersionChecking.LATEST_VERSION}", EditorGUIUtility.IconContent("d_Package Manager").image);

                GUIStyle linkStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                linkStyle.normal.textColor = Color.Lerp(LightBlueColor, LightBlueColor * 1.4f,
                    Mathf.Sin((float)EditorApplication.timeSinceStartup * 5f) * 0.5f + 0.5f);
                linkStyle.hover.textColor = LighterBlueColor;

                float updateWidth = linkStyle.CalcSize(update).x;

                Rect updateRect = new Rect(rect.x + (rect.width - updateWidth) * 0.5f, rect.y + titleSize.y + 1f, updateWidth, titleSize.y);

                //Make it clickable
                EditorGUIUtility.AddCursorRect(updateRect, MouseCursor.Link);

                if (GUI.Button(updateRect, update, linkStyle))
                {
                    //Because update checking only occurs when the editor starts, assume the user will update, so mark it accordingly.
                    AssetInfo.VersionChecking.UPDATE_AVAILABLE = false;
                
                    AssetInfo.OpenInPackageManager();
                }
                
            }
            
            EditorGUILayout.Space(10f);
        }
        
        public static void DrawRenderGraphError()
        {
            #if UNITY_6000_0_OR_NEWER && !UNITY_6000_3_OR_NEWER && URP
            if (PipelineUtilities.RenderGraphEnabled() == false)
            {
                EditorGUILayout.HelpBox("Render Graph is disabled, but is required." +
                                        "\n\nBackwards compatibility mode must be disabled.", MessageType.Error);
                
                GUILayout.Space(-32);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("Fix", EditorGUIUtility.IconContent("d_tab_next").image), GUILayout.Width(60)))
                    {
                        RenderGraphSettings settings = UnityEngine.Rendering.GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
                        
                        settings.enableRenderCompatibilityMode = false;

                        EditorUtility.DisplayDialog($"{AssetInfo.ASSET_NAME} v{AssetInfo.INSTALLED_VERSION}", 
                            "Please note that this option will be removed in a future Unity version, rendering this version of the asset will no longer functional.", "OK");
                    }
                    GUILayout.Space(8);
                }
                GUILayout.Space(11);
            }
            #endif
        }

        public static void DrawFooter()
        {
            GUILayout.Space(5f);

            Rect r = EditorGUILayout.GetControlRect();
            
            if (r.Contains(Event.current.mousePosition))
            {
                //EditorGUIUtility.AddCursorRect(new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y, 27, 27), MouseCursor.Link);
            }
            
            if (GUI.Button(r,new GUIContent("- Staggart Creations -", "Open website"), EditorStyles.centeredGreyMiniLabel))
            {
                //Application.OpenURL("http://staggart.xyz");
            }
        }
        
        public static void DrawActionBox(string text, string label, MessageType messageType, Action action)
        {
            DrawActionBox(text, new GUIContent(label), messageType, action);
        }

        public static void DrawActionBox(string text, GUIContent buttonContent, MessageType messageType, Action action)
        {
            Assert.IsNotNull(action);

            EditorGUILayout.HelpBox(text, messageType);

            GUILayout.Space(-32);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button(buttonContent, GUILayout.Width(EditorStyles.miniButton.CalcSize(new GUIContent(buttonContent)).x + 5f)))
                    action();

                GUILayout.Space(8);
            }
            GUILayout.Space(11);
        }

        private static float Sin(float offset = 0f)
        {
            return Mathf.Sin(offset + (float)EditorApplication.timeSinceStartup * Mathf.PI * 2f) * 0.5f + 0.5f;
        }
        
        public static void DrawNotification(string text, MessageType messageType = MessageType.None)
        {
            DrawHelpbox(text, messageType);
        }
        
        public static void DrawNotification(bool condition, string text, string buttonLabel, Action action, MessageType messageType = MessageType.None)
        {
            DrawHelpbox(text, messageType, condition, buttonLabel, action);
        }
        
        public static void DrawNotification(bool condition, string text, MessageType messageType = MessageType.None)
        {
            DrawHelpbox(text, messageType, condition, null, null);
        }
        
        private static void DrawHelpbox(string text, MessageType messageType = MessageType.None, bool condition = true, string buttonLabel = "", Action action = null)
        {
            if (!condition) return;
            
            Rect r = EditorGUILayout.GetControlRect();
            r.width -= 10f;

            Color sideColor = Color.gray;
            Texture icon = null;
            switch (messageType)
            {
                case (MessageType.None):
                    {
                        sideColor = Color.gray;
                    }
                    break;
                case (MessageType.Warning): //Warning
                    {
                        sideColor = Color.Lerp(OrangeColor, OrangeColor * 1.20f, Sin(r.y));
                        icon = WarningIcon;
                    }
                    break;
                case (MessageType.Error): //Error
                    {
                        sideColor = Color.Lerp(RedColor, RedColor * 1.33f, Sin(r.y));
                        icon = ErrorIcon;
                    }
                    break;
                case (MessageType.Info): //Info
                    {
                        sideColor = Color.Lerp(new Color(1f, 1f, 1f), new Color(0.9f, 0.9f, 0.9f), Sin(r.y));
                        icon = InfoIcon;
                    }
                    break;
            }
            
            float width = r.width;
            //float rightPadding = 
            float height = EditorStyles.helpBox.CalcHeight(new GUIContent(text), EditorGUIUtility.currentViewWidth) + (EditorStyles.label.lineHeight * 2f);
            r.height = height;

            Rect btnRect = r;
            GUIContent btnContent = null;
            //Showing a button instead
            if (action != null)
            {
                icon = null;

                btnContent = new GUIContent(" " + buttonLabel, EditorGUIUtility.IconContent("SceneLoadIn").image, "Execute suggested action");
                float size = EditorStyles.toolbarButton.CalcSize(btnContent).x + 5f;
                btnRect.width = size;
                btnRect.x = width - size;
                btnRect.height = EditorStyles.miniButtonMid.fixedHeight+5f;
                //Vertical center
                btnRect.y += (height * 0.5f) - (btnRect.height * 0.5f);
            }

            Rect iconRect = r;
            if (icon != null) 
            {
                float size = Mathf.Min(height * 0.75f, 50f);
                iconRect = r;
                iconRect.x = r.width - size;
                iconRect.width = size;
                iconRect.height = iconRect.width;
                //Vertical center
                iconRect.y += (height * 0.5f) - (iconRect.height * 0.5f);
                
                //Recalculate text height
                height = EditorStyles.helpBox.CalcHeight(new GUIContent(text),
                    EditorGUIUtility.currentViewWidth - size) + (EditorStyles.label.lineHeight * 2f);
                r.height = height;
            }

            float backgroundTint = EditorGUIUtility.isProSkin ? 0.4f : 1f;
            EditorGUI.DrawRect(r, new Color(backgroundTint, backgroundTint, backgroundTint, 0.2f));

            Rect colorRect = r;
            colorRect.width = 7f;

            EditorGUI.DrawRect(colorRect, sideColor);

            Rect textRect = r;
            textRect.x += colorRect.width + 10f;

            //Shrink text area on right side to make room
            if (icon != null) textRect.width -= iconRect.width * 2f;
            if(action != null) textRect.width -= btnRect.width + 50f;

            GUI.Label(textRect, new GUIContent(text), Styles.NotificationArea);

            if (icon != null)
            {
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            }

            if (action != null)
            {
                if (GUI.Button(btnRect, btnContent))
                {
                    action?.Invoke();
                }
            }

            GUILayout.Space(height - EditorStyles.label.lineHeight); //17=default line height
        }
        
        public static bool DrawSetupItem(Installer.SetupItem item)
        {
            Texture icon = Styles.CheckMark;

            switch (item.state)
            {
                case MessageType.Error: icon = Styles.ErrorIcon; break;
                case MessageType.Warning: icon = Styles.WarningIcon; break;
                case MessageType.None: icon = Styles.CheckMark; break;
                case MessageType.Info: icon = Styles.InfoIcon; break;
            }
            
            
            using (new EditorGUILayout.HorizontalScope(EditorStyles.label))
            {
                Color defaultColor = GUI.contentColor;

                switch (item.state)
                {
                    case MessageType.Error: GUI.contentColor = UI.RedColor;
                        break;                    
                    case MessageType.Warning: GUI.contentColor = UI.OrangeColor;
                        break;
                    case MessageType.None: GUI.contentColor = UI.GreenColor;
                        break;
                }
                
                EditorGUILayout.LabelField(new GUIContent("  " + item.name, icon), EditorStyles.boldLabel);
                
                GUI.contentColor = defaultColor;
                
                if (item.action != null)
                {
                    if (GUILayout.Button(item.actionName))
                    {
                        item.ExecuteAction();
                        return true;
                    }
                }
            }

            GUILayout.Space(-3f);
            
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(25f);
                EditorGUILayout.LabelField(item.description, Styles.WordWrapLabel);
            }
            
            GUILayout.Space(-7f);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider, GUILayout.Height(12f));
            
            GUILayout.Space(-3f);
            
            //No action clicked
            return false;
        }
        
        public static void DrawH2(string text)
        {
            Rect backgroundRect = EditorGUILayout.GetControlRect();
            backgroundRect.height = 25f;
            
            var labelRect = backgroundRect;

            // Background rect should be full-width
            backgroundRect.xMin = 0f;

            // Background
            float backgroundTint = (EditorGUIUtility.isProSkin ? 0.1f : 1f);
            EditorGUI.DrawRect(backgroundRect, new Color(backgroundTint, backgroundTint, backgroundTint, 0.2f));

            // Title
            EditorGUI.LabelField(labelRect, new GUIContent(text), Styles.H2);
            
            EditorGUILayout.Space(backgroundRect.height * 0.5f);
        }
        
        
        public static void DrawIntegration(RendererIntegration.Integration integration)
        {
            EditorGUILayout.LabelField(integration.name, EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            {
                Color defaultColor = GUI.contentColor;

                GUILayout.Label(integration.thumbnail, GUILayout.Height(50f), GUILayout.Width(50f));

                EditorGUILayout.BeginVertical();
                {
                    GUILayout.Space(5);

                    if (integration.asset != RendererIntegration.Assets.None)
                    {
                        using (new EditorGUILayout.HorizontalScope(EditorStyles.textField))
                        {
                            if (integration.installed) GUI.contentColor = GreenColor;
                            if (!integration.installed) GUI.contentColor = UI.OrangeColor;
                            EditorGUILayout.LabelField(integration.installed ? "Installed" : "Not Installed", GUILayout.MaxWidth((75f)));
                            GUI.contentColor = defaultColor;

                            GUILayout.FlexibleSpace();
                            
                            if (GUILayout.Button(UI.Styles.AssetStoreBtnContent))
                            {
                                AssetInfo.OpenAssetStore($"https://assetstore.unity.com/packages/slug/{integration.id}");
                            }
                        }

                        GUILayout.Space(5);
                    }

                    if (!integration.installed)
                    {
                        //UI.DrawNotification("Shader library for asset not found in project", MessageType.Error);
                    }
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();
        }

        
        public static class Material
        {
            //Section toggles
            public class Section
            {
                private const float ANIMATION_SPEED = 16f;
                
                public bool Expanded
                {
                    get { return SessionState.GetBool(id, false); }
                    set { SessionState.SetBool(id, value); }
                }

                public AnimBool anim;

                private readonly string id;
                public string title;

                public Section(MaterialEditor target, string id, string title)
                {
                    this.id = "SGS_" + id + "_SECTION";
                    this.title = title;

                    anim = new AnimBool(false);
                    anim.valueChanged.AddListener(target.Repaint);
                    anim.speed = ANIMATION_SPEED;
                }

                public void SetTarget()
                {
                    anim.target = Expanded;
                }
            }

            //https://github.com/Unity-Technologies/Graphics/blob/d0473769091ff202422ad13b7b764c7b6a7ef0be/com.unity.render-pipelines.core/Editor/CoreEditorUtils.cs#L460
            public static bool DrawHeader(string title, bool isExpanded, Action clickAction = null)
            {
#if URP
                CoreEditorUtils.DrawSplitter();
#endif

                var backgroundRect = GUILayoutUtility.GetRect(1f, 25f);
 
                var labelRect = backgroundRect;
                labelRect.xMin += 8f;
                labelRect.xMax -= 20f + 16 + 5;

                var foldoutRect = backgroundRect;
                
                #if UNITY_2022_1_OR_NEWER
                //As of this version extra padding is added, to make room for property override indicators
                foldoutRect.x -= 16f;
                #endif
                
                foldoutRect.xMin -= 8f;
                foldoutRect.y += 0f;
                foldoutRect.width = 25f;
                foldoutRect.height = 25f;

                // Background rect should be full-width
                backgroundRect.xMin = 0f;
                backgroundRect.width += 4f;

                // Background
                float backgroundTint = EditorGUIUtility.isProSkin ? 0.1f : 1f;
                EditorGUI.DrawRect(backgroundRect, new Color(backgroundTint, backgroundTint, backgroundTint, 0.2f));

                // Title
                EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);

                // Foldout
                isExpanded = GUI.Toggle(foldoutRect, isExpanded, new GUIContent(isExpanded ? "−" : "≡"), EditorStyles.boldLabel);

                // Context menu
                #if URP
                var menuIcon = CoreEditorStyles.paneOptionsIcon;
#else
                Texture menuIcon = null;
#endif
                var menuRect = new Rect(labelRect.xMax + 3f + 16 + 5, labelRect.y + 1f, menuIcon.width, menuIcon.height);

                //if (clickAction != null)
                //GUI.DrawTexture(menuRect, menuIcon);

                // Handle events
                var e = Event.current;

                if (e.type == EventType.MouseDown)
                {
                    if (clickAction != null && menuRect.Contains(e.mousePosition))
                    {
                        e.Use();
                    }
                    else if (labelRect.Contains(e.mousePosition))
                    {
                        if (e.button == 0)
                        {
                            isExpanded = !isExpanded;
                            if (clickAction != null) clickAction.Invoke();
                        }

                        e.Use();
                    }
                }

#if URP
                //CoreEditorUtils.DrawSplitter();
#endif

                //GUILayout.Space(5f);

                return isExpanded;
            }
        }

        public class Styles
        {
            public static GUIStyle _NotificationArea;

            public static GUIStyle NotificationArea
            {
                get
                {
                    if (_NotificationArea == null)
                    {
                        _NotificationArea = new GUIStyle(EditorStyles.label)
                        {
                            //margin = new RectOffset(15, 0, 15, 0),
                            //padding = new RectOffset(5, 5, 5, 5),
                            richText = true,
                            wordWrap = true,
                            clipping = TextClipping.Overflow,
                        };
                    }

                    return _NotificationArea;
                }
            }

            private static Texture _CheckMark;

            public static Texture CheckMark
            {
                get
                {
                    if (_CheckMark == null)
                    {
                        _CheckMark = EditorGUIUtility.IconContent("TestPassed").image;

                    }

                    return _CheckMark;
                }
            }

            private static Texture _InfoIcon;

            public static Texture InfoIcon
            {
                get
                {
                    if (_InfoIcon == null)
                    {
                        _InfoIcon = EditorGUIUtility.IconContent("console.infoicon.sml").image;
                    }

                    return _InfoIcon;
                }
            }

            private static Texture _ErrorIcon;

            public static Texture ErrorIcon
            {
                get
                {
                    if (_ErrorIcon == null)
                    {
                        _ErrorIcon = EditorGUIUtility.IconContent("console.erroricon.sml").image;
                    }

                    return _ErrorIcon;
                }
            }

            private static Texture _WarningIcon;

            public static Texture WarningIcon
            {
                get
                {
                    if (_WarningIcon == null)
                    {
                        _WarningIcon = EditorGUIUtility.IconContent("console.warnicon.sml").image;
                    }

                    return _WarningIcon;
                }
            }


            private static GUIStyle _UpdateText;

            public static GUIStyle UpdateText
            {
                get
                {
                    if (_UpdateText == null)
                    {
                        _UpdateText = new GUIStyle("Button")
                        {
                            //fontSize = 10,
                            alignment = TextAnchor.MiddleLeft,
                            stretchWidth = false,
                        };
                    }

                    return _UpdateText;
                }
            }

            private static GUIStyle _Footer;

            public static GUIStyle Footer
            {
                get
                {
                    if (_Footer == null)
                    {
                        _Footer = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                        {
                            richText = true,
                            alignment = TextAnchor.MiddleCenter,
                            wordWrap = true,
                            fontSize = 12
                        };
                    }

                    return _Footer;
                }
            }

            private static GUIStyle _Button;

            public static GUIStyle Button
            {
                get
                {
                    if (_Button == null)
                    {
                        _Button = new GUIStyle(GUI.skin.button)
                        {
                            alignment = TextAnchor.MiddleLeft,
                            stretchWidth = true,
                            richText = true,
                            wordWrap = true,
                            padding = new RectOffset()
                            {
                                left = 7,
                                right = 0,
                                top = 5,
                                bottom = 5
                            },
                            imagePosition = ImagePosition.ImageLeft
                        };
                    }

                    return _Button;
                }
            }

            private static GUIContent _AssetStoreBtnContent;

            public static GUIContent AssetStoreBtnContent
            {
                get
                {
                    if (_AssetStoreBtnContent == null)
                    {
                        _AssetStoreBtnContent = new GUIContent("  View on Asset Store ",
                            EditorGUIUtility.IconContent("Asset Store").image,
                            "Open web page.\n\nURL may contain an affiliate ID, this commission helps to fund the purchase of new assets in order to investigate/develop integrations for them.");
                    }

                    return _AssetStoreBtnContent;
                }
            }

            private static GUIStyle _H1;

            public static GUIStyle H1
            {
                get
                {
                    if (_H1 == null)
                    {
                        _H1 = new GUIStyle(GUI.skin.label)
                        {
                            richText = true,
                            alignment = TextAnchor.MiddleCenter,
                            wordWrap = true,
                            fontSize = 18,
                            fontStyle = FontStyle.Normal
                        };
                    }

                    return _H1;
                }
            }

            private static GUIStyle _H2;

            public static GUIStyle H2
            {
                get
                {
                    if (_H2 == null)
                    {
                        _H2 = new GUIStyle(GUI.skin.label)
                        {
                            richText = true,
                            alignment = TextAnchor.MiddleLeft,
                            wordWrap = true,
                            fontSize = 14,
                            fontStyle = FontStyle.Bold
                        };
                    }

                    return _H2;
                }
            }

            private static GUIStyle _Section;

            public static GUIStyle Section
            {
                get
                {
                    if (_Section == null)
                    {
                        _Section = new GUIStyle(EditorStyles.helpBox)
                        {
                            margin = new RectOffset(0, 0, -5, 5),
                            padding = new RectOffset(10, 10, 5, 5),
                            clipping = TextClipping.Clip,
                        };
                    }

                    return _Section;
                }
            }

            private static GUIStyle _WordWrapLabel;

            public static GUIStyle WordWrapLabel
            {
                get
                {
                    if (_WordWrapLabel == null)
                    {
                        _WordWrapLabel = new GUIStyle(EditorStyles.label);
                        _WordWrapLabel.wordWrap = true;
                        _WordWrapLabel.richText = true;
                    }

                    return _WordWrapLabel;
                }
            }

            private static GUIStyle _BoldLabel;

            public static GUIStyle BoldLabel
            {
                get
                {
                    if (_BoldLabel == null)
                    {
                        _BoldLabel = new GUIStyle(EditorStyles.largeLabel);
                        _BoldLabel.fontStyle = FontStyle.Bold;
                    }

                    return _BoldLabel;
                }
            }

            private static GUIStyle _Tab;

            public static GUIStyle Tab
            {
                get
                {
                    if (_Tab == null)
                    {
                        _Tab = new GUIStyle(EditorStyles.miniButtonMid)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            stretchWidth = true,
                            richText = true,
                            wordWrap = true,
                            fontSize = 16,
                            fixedHeight = 27.5f,
                            fontStyle = FontStyle.Bold,
                            padding = new RectOffset()
                            {
                                left = 14,
                                right = 14,
                                top = 3,
                                bottom = 3
                            }
                        };
                    }

                    return _Tab;
                }
            }

            private static GUIStyle s_CenterBoldLabel;

            public static GUIStyle CenterBoldLabel
            {
                get
                {
                    if (s_CenterBoldLabel == null)
                    {
                        s_CenterBoldLabel = new GUIStyle(EditorStyles.largeLabel);
                        s_CenterBoldLabel.alignment = TextAnchor.UpperCenter;
                        s_CenterBoldLabel.padding = new RectOffset();
                        s_CenterBoldLabel.fontStyle = FontStyle.Bold;
                    }

                    return s_CenterBoldLabel;
                }
            }

            private static GUIStyle s_AddOnTitle;

            private static GUIStyle AddOnTitle
            {
                get
                {
                    if (s_AddOnTitle == null)
                    {
                        s_AddOnTitle = new GUIStyle(CenterBoldLabel);
                        s_AddOnTitle.fontSize = 14;
                        s_AddOnTitle.alignment = TextAnchor.MiddleLeft;
                    }

                    return s_AddOnTitle;
                }
            }
        }

    }
}