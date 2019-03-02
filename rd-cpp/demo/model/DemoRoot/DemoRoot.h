#ifndef DemoRoot_H
#define DemoRoot_H

#include "Buffer.h"
#include "Identities.h"
#include "MessageBroker.h"
#include "Protocol.h"
#include "RdId.h"
#include "RdList.h"
#include "RdMap.h"
#include "RdProperty.h"
#include "RdSet.h"
#include "RdSignal.h"
#include "RName.h"
#include "ISerializable.h"
#include "Polymorphic.h"
#include "NullableSerializer.h"
#include "ArraySerializer.h"
#include "SerializationCtx.h"
#include "Serializers.h"
#include "ISerializersOwner.h"
#include "IUnknownInstance.h"
#include "RdExtBase.h"
#include "RdCall.h"
#include "RdEndpoint.h"
#include "RdTask.h"
#include "gen_util.h"

#include <iostream>
#include <cstring>
#include <cstdint>
#include <vector>
#include <type_traits>
#include <utility>

#include "optional.hpp"


#pragma warning( push )
#pragma warning( disable:4250 )
class DemoRoot : public rd::RdExtBase
{
    
    //companion
    
    public:
    struct DemoRootSerializersOwner : public rd::ISerializersOwner {
        void registerSerializersCore(rd::Serializers const& serializers);
    };
    
    static DemoRootSerializersOwner serializersOwner;
    
    
    public:
    void connect(rd::Lifetime lifetime, rd::IProtocol const * protocol);
    
    
    //custom serializers
    private:
    
    //fields
    protected:
    
    //initializer
    private:
    void initialize();
    
    //primary ctor
    public:
    explicit DemoRoot();
    
    
    //default ctors and dtors
    
    
    DemoRoot(DemoRoot &&) = delete;
    
    DemoRoot& operator=(DemoRoot &&) = delete;
    
    virtual ~DemoRoot() = default;
    
    //reader
    
    //writer
    
    //virtual init
    void init(rd::Lifetime lifetime) const override;
    
    //identify
    void identify(const rd::IIdentities &identities, rd::RdId const &id) const override;
    
    //getters
    
    //intern
    
    //equals trait
    private:
    bool equals(rd::IPolymorphicSerializable const& object) const;
    
    //equality operators
    public:
    friend bool operator==(const DemoRoot &lhs, const DemoRoot &rhs);
    friend bool operator!=(const DemoRoot &lhs, const DemoRoot &rhs);
    
    //hash code trait
    
    //type name trait
};

#pragma warning( pop )


//hash code trait

#endif // DemoRoot_H